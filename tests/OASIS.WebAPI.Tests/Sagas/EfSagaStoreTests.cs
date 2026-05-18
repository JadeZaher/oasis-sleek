using FluentAssertions;
using OASIS.WebAPI.Sagas;
using OASIS.WebAPI.Tests.TestSupport;

namespace OASIS.WebAPI.Tests.Sagas;

/// <summary>
/// Store-level proofs of the conditional-transition primitives directly against
/// SQLite (file-backed → genuine DB write-lock arbitration, the same fidelity
/// the api-safety-hardening conditional-UPDATE tests rely on). These are the
/// single-winner / lease-reclaim / backoff building blocks the processor
/// composes.
/// </summary>
public class EfSagaStoreTests
{
    private const string Saga = SampleCounterSaga.Name;

    [Fact]
    public async Task TryClaim_NotDueYet_ReturnsNull()
    {
        using var h = new SagaTestHarness();
        var rec = await h.Store.EnqueueAsync(
            Saga, "increment", "c1", "saga:c1:increment", "{}", false, CancellationToken.None);

        // NextRunAt == now (enqueue); claim with an earlier 'now' ⇒ not due.
        var claimed = await h.Store.TryClaimDueStepAsync(
            rec.Id, rec.NextRunAt.AddSeconds(-5), CancellationToken.None);

        claimed.Should().BeNull();
        var row = await h.Store.GetAsync(rec.Id, CancellationToken.None);
        row!.Status.Should().Be(StepStatus.Pending);
    }

    [Fact]
    public async Task ScheduleRetry_OnlyAppliesToInProgress_AndAdvancesNextRunAt()
    {
        using var h = new SagaTestHarness();
        var rec = await h.Store.EnqueueAsync(
            Saga, "increment", "c2", "saga:c2:increment", "{}", false, CancellationToken.None);

        // Not InProgress yet ⇒ conditional retry must NOT apply.
        var noop = await h.Store.ScheduleRetryAsync(
            rec.Id, DateTime.UtcNow.AddMinutes(1), "x", CancellationToken.None);
        noop.Should().BeFalse();

        await h.Store.TryClaimDueStepAsync(rec.Id, DateTime.UtcNow, CancellationToken.None);
        var next = DateTime.UtcNow.AddMinutes(3);
        var ok = await h.Store.ScheduleRetryAsync(rec.Id, next, "boom", CancellationToken.None);

        ok.Should().BeTrue();
        var row = await h.Store.GetAsync(rec.Id, CancellationToken.None);
        row!.Status.Should().Be(StepStatus.Pending);
        row.AttemptCount.Should().Be(1);
        row.NextRunAt.Should().BeCloseTo(next, TimeSpan.FromSeconds(2));
        row.LastError.Should().Be("boom");
        row.ClaimedAt.Should().BeNull();
    }

    [Fact]
    public async Task LeaseReclaim_ReturnsCrashedInProgressStepToPendingAndDue()
    {
        using var h = new SagaTestHarness();
        var rec = await h.Store.EnqueueAsync(
            Saga, "increment", "c3", "saga:c3:increment", "{}", false, CancellationToken.None);

        var claimAt = DateTime.UtcNow;
        await h.Store.TryClaimDueStepAsync(rec.Id, claimAt, CancellationToken.None);

        // Lease 30s; the claim is fresh (ClaimedAt≈now) so within lease ⇒ NOT
        // reclaimed and NOT due (a healthy in-flight processor owns it).
        var fresh = await h.Store.GetDueStepIdsAsync(
            claimAt.AddSeconds(1), 10, TimeSpan.FromSeconds(30),
            CancellationToken.None);
        fresh.Should().NotContain(rec.Id);

        // Evaluate 'now' well past the lease (ClaimedAt older than lease) ⇒ the
        // crashed processor's row is reclaimed (Pending + due) and listed.
        var due = await h.Store.GetDueStepIdsAsync(
            claimAt.AddSeconds(40), 10, TimeSpan.FromSeconds(30),
            CancellationToken.None);
        due.Should().Contain(rec.Id);

        var row = await h.Store.GetAsync(rec.Id, CancellationToken.None);
        row!.Status.Should().Be(StepStatus.Pending);
        row.ClaimedAt.Should().BeNull();
    }

    [Fact]
    public async Task Compensate_TransitionsForwardToCompensating_AndEnqueuesCompensationRow()
    {
        using var h = new SagaTestHarness();
        var rec = await h.Store.EnqueueAsync(
            Saga, "increment", "c4", "saga:c4:increment", "{\"Bucket\":\"b\",\"By\":1}",
            false, CancellationToken.None);
        await h.Store.TryClaimDueStepAsync(rec.Id, DateTime.UtcNow, CancellationToken.None);

        var comp = await h.Store.CompensateStepAsync(
            rec.Id, SampleCounterSaga.StepCompensate,
            "saga:c4:compensate-decrement", rec.Payload, "exhausted",
            CancellationToken.None);

        comp.Should().NotBeNull();
        comp!.IsCompensation.Should().BeTrue();
        comp.Status.Should().Be(StepStatus.Pending);
        comp.StepIdempotencyKey.Should().Be("saga:c4:compensate-decrement");

        var fwd = await h.Store.GetAsync(rec.Id, CancellationToken.None);
        fwd!.Status.Should().Be(StepStatus.Compensating);
    }
}
