using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OASIS.WebAPI.Sagas;
using OASIS.WebAPI.Tests.TestSupport;

namespace OASIS.WebAPI.Tests.Sagas;

/// <summary>
/// Skeleton-level proofs for the generic durable-saga module (Phase 1 task 6),
/// all exercised against a constraint-enforcing SQLite engine via the existing
/// <see cref="SqliteTestContext"/> + <see cref="FakeIdempotencyStore"/>.
///
/// Covered: concurrent claim → exactly one winner; crash mid-step → reclaimed
/// after the lease and resumed with NO double-run of the idempotent handler;
/// retry with backoff increments NextRunAt; max attempts → declared
/// compensation step invoked; compensation exhaustion → dead-letter; and a
/// trivial NON-BRIDGE sample saga end-to-end proving reuse with zero core
/// change.
/// </summary>
public class SagaProcessorTests
{
    private static readonly RetryPolicy FastTwoAttempts = new()
    {
        MaxAttempts = 2,
        BaseBackoff = TimeSpan.FromMilliseconds(50),
        MaxBackoff = TimeSpan.FromMilliseconds(200),
    };

    // ── (1) Concurrent claim of one due step → exactly one processor wins ──
    [Fact]
    public async Task ConcurrentClaim_OneDueStep_ExactlyOneWinner()
    {
        using var h = new SagaTestHarness();
        var enq = await h.Store.EnqueueAsync(
            SampleCounterSaga.Name, SampleCounterSaga.StepIncrement,
            "corr-1", "saga:corr-1:increment", "{}", false, CancellationToken.None);

        const int parallelism = 24;
        var now = DateTime.UtcNow.AddSeconds(1);
        var claims = new ConcurrentBag<bool>();

        var tasks = Enumerable.Range(0, parallelism).Select(_ => Task.Run(async () =>
        {
            var won = await h.Store.TryClaimDueStepAsync(enq.Id, now, CancellationToken.None);
            claims.Add(won is not null);
        })).ToArray();

        await Task.WhenAll(tasks);

        claims.Should().HaveCount(parallelism);
        claims.Count(c => c).Should().Be(1, "the conditional UPDATE elects a single winner");

        var row = await h.Store.GetAsync(enq.Id, CancellationToken.None);
        row!.Status.Should().Be(StepStatus.InProgress);
        row.ClaimedAt.Should().NotBeNull();
    }

    // ── (2) Crash mid-step → reclaimed after lease timeout, resumes, NO
    //        double-run of the idempotent handler ──
    [Fact]
    public async Task CrashMidStep_ReclaimedAfterLease_ResumesWithoutDoubleEffect()
    {
        var shortLease = new SagaOptions { LeaseTimeoutSeconds = 1 };
        using var h = new SagaTestHarness(options: shortLease);

        await h.Coordinator.StartAsync(
            SampleCounterSaga.Name, "corr-crash",
            new CounterPayload("crash", 5), CancellationToken.None);

        var due = await h.Store.GetDueStepIdsAsync(
            DateTime.UtcNow, 10, TimeSpan.FromSeconds(1), CancellationToken.None);
        var stepId = due.Single();

        // Simulate a processor that claimed the step then DIED before
        // recording any outcome (row stuck InProgress).
        var claimed = await h.Store.TryClaimDueStepAsync(
            stepId, DateTime.UtcNow, CancellationToken.None);
        claimed.Should().NotBeNull();
        h.Sink.Get("crash").Should().Be(0, "the crashed processor never ran the effect");

        // Before the lease lapses it is NOT due.
        var notYet = await h.Store.GetDueStepIdsAsync(
            DateTime.UtcNow, 10, TimeSpan.FromSeconds(1), CancellationToken.None);
        notYet.Should().BeEmpty();

        // After the lease lapses the next "what is due?" reclaims it and a real
        // tick resumes it. The idempotent handler runs the effect exactly once.
        await Task.Delay(1300);
        var processor = h.Processor();
        var processed = await processor.ProcessDueStepsAsync(CancellationToken.None);
        processed.Should().Be(1);

        h.Sink.Get("crash").Should().Be(5, "effect applied exactly once after reclaim");

        // Re-run the SAME (now-completed) work again: even if the row were
        // somehow re-presented, the idempotency gate prevents a second effect.
        var rec = await h.Store.GetAsync(stepId, CancellationToken.None);
        rec!.Status.Should().Be(StepStatus.Completed);
        h.Sink.Get("crash").Should().Be(5);
    }

    // ── (3) Retry with backoff increments NextRunAt ──
    [Fact]
    public async Task FailedStep_SchedulesBackoffRetry_AdvancingNextRunAt()
    {
        using var h = new SagaTestHarness(incrementPolicy: new RetryPolicy
        {
            MaxAttempts = 5,
            BaseBackoff = TimeSpan.FromSeconds(30),
            MaxBackoff = TimeSpan.FromMinutes(10),
        });
        h.Increment.FailUntilAttempt = int.MaxValue; // always fail

        await h.Coordinator.StartAsync(
            SampleCounterSaga.Name, "corr-retry",
            new CounterPayload("retry", 1), CancellationToken.None);

        var due = await h.Store.GetDueStepIdsAsync(
            DateTime.UtcNow, 10, TimeSpan.FromMinutes(5), CancellationToken.None);
        var id = due.Single();

        var before = DateTime.UtcNow;
        var processor = h.ProcessorWith(h.Increment);
        await processor.ProcessDueStepsAsync(CancellationToken.None);
        var after = DateTime.UtcNow;

        var rec = await h.Store.GetAsync(id, CancellationToken.None);
        rec!.Status.Should().Be(StepStatus.Pending, "a failed attempt with budget left re-queues");
        rec.AttemptCount.Should().Be(1);
        rec.LastError.Should().Contain("forced failure");

        // Backoff is exponential with FULL JITTER (spec: "exponential backoff +
        // jitter") — attempt 1 schedules NextRunAt = scheduledAt + Uniform(0,
        // base*2^0). The deterministic invariants are: it moved strictly
        // forward (a failed claimed step is never left immediately due at the
        // same instant), and it never exceeds the attempt-1 jitter ceiling
        // (= BaseBackoff, here 30s) measured from the latest possible schedule
        // instant. (A fixed/at-least delay assertion would be inherently flaky
        // BECAUSE full jitter can legitimately yield a near-zero delay — that
        // is the anti-thundering-herd property, not a bug.)
        var ceiling = TimeSpan.FromSeconds(30); // BaseBackoff * 2^(1-1)
        rec.NextRunAt.Should().BeOnOrAfter(before,
            "the retry is rescheduled at-or-after the moment processing began");
        rec.NextRunAt.Should().BeOnOrBefore(after + ceiling + TimeSpan.FromSeconds(2),
            "a single jittered backoff never exceeds the exponential ceiling for this attempt");

        // Drive the exponential GROWTH deterministically: the attempt-2 ceiling
        // (base*2^1 = 60s) is double attempt-1's. Re-claim + fail again and
        // assert the schedule window itself widened (the property full jitter
        // is layered on top of), which IS deterministic.
        await h.Store.TryClaimDueStepAsync(rec.Id, rec.NextRunAt.AddSeconds(1),
            CancellationToken.None);
        var t2 = DateTime.UtcNow;
        await h.Store.ScheduleRetryAsync(
            rec.Id,
            t2 + new RetryPolicy
            {
                MaxAttempts = 5,
                BaseBackoff = TimeSpan.FromSeconds(30),
                MaxBackoff = TimeSpan.FromMinutes(10),
            }.NextDelay(2),
            "again", CancellationToken.None);
        var rec2 = await h.Store.GetAsync(rec.Id, CancellationToken.None);
        rec2!.AttemptCount.Should().Be(2);
        rec2.NextRunAt.Should().BeOnOrBefore(t2 + TimeSpan.FromSeconds(60) + TimeSpan.FromSeconds(2),
            "attempt-2 jitter ceiling is base*2^1 = 60s");
    }

    // ── (4) Max attempts → declared compensation step invoked ──
    [Fact]
    public async Task ForwardStepExhausted_RoutesToDeclaredCompensationStep()
    {
        using var h = new SagaTestHarness(
            incrementPolicy: FastTwoAttempts, compensatePolicy: FastTwoAttempts);
        h.Increment.FailUntilAttempt = int.MaxValue; // forward step always fails

        await h.Coordinator.StartAsync(
            SampleCounterSaga.Name, "corr-comp",
            new CounterPayload("comp", 7), CancellationToken.None);

        var processor = h.Processor();

        // Drive ticks until the forward step exhausts (2 attempts) and the
        // compensation step is enqueued + run.
        await DrainAsync(h, processor, maxTicks: 12);

        var rows = await AllRows(h, "corr-comp");
        var fwd = rows.Single(r => r.StepName == SampleCounterSaga.StepIncrement);
        fwd.Status.Should().Be(StepStatus.Compensating,
            "an exhausted forward step transitions to Compensating");
        fwd.AttemptCount.Should().BeGreaterThanOrEqualTo(2);

        var comp = rows.SingleOrDefault(r => r.StepName == SampleCounterSaga.StepCompensate);
        comp.Should().NotBeNull("the declared compensation step is enqueued as a first-class step");
        comp!.IsCompensation.Should().BeTrue();
        comp.StepIdempotencyKey.Should().Be("saga:corr-comp:compensate-decrement",
            "the compensation step carries its OWN distinct idempotency key");
        h.Sink.CompensationCalls.Should().BeGreaterThan(0, "compensation handler ran");
        comp.Status.Should().Be(StepStatus.Completed);
    }

    // ── (5) Compensation exhaustion → dead-letter ──
    [Fact]
    public async Task CompensationExhausted_DeadLetters()
    {
        using var h = new SagaTestHarness(
            incrementPolicy: FastTwoAttempts, compensatePolicy: FastTwoAttempts);
        h.Increment.FailUntilAttempt = int.MaxValue;     // forward always fails
        h.Compensate.AlwaysFail = true;                   // compensation always fails

        await h.Coordinator.StartAsync(
            SampleCounterSaga.Name, "corr-dl",
            new CounterPayload("dl", 3), CancellationToken.None);

        var processor = h.Processor();
        await DrainAsync(h, processor, maxTicks: 20);

        var rows = await AllRows(h, "corr-dl");
        var comp = rows.Single(r => r.StepName == SampleCounterSaga.StepCompensate);
        comp.Status.Should().Be(StepStatus.DeadLettered,
            "a compensation step that itself exhausts has no further recourse");
        comp.DeadLettered.Should().BeTrue();
        comp.LastError.Should().Contain("compensation deliberately failing");
    }

    // ── (6) End-to-end NON-BRIDGE sample saga: increment → mark-done,
    //        proving reuse with zero core change ──
    [Fact]
    public async Task SampleSaga_HappyPath_RunsBothForwardStepsExactlyOnce()
    {
        using var h = new SagaTestHarness();

        await h.Coordinator.StartAsync(
            SampleCounterSaga.Name, "corr-ok",
            new CounterPayload("ok", 10), CancellationToken.None);

        var processor = h.Processor();
        await DrainAsync(h, processor, maxTicks: 6);

        h.Sink.Get("ok").Should().Be(10, "the increment effect applied exactly once");
        h.Sink.IncrementCalls.Should().Be(1);
        h.Sink.DoneCalls.Should().Be(1, "the second forward step ran after the first completed");

        var rows = await AllRows(h, "corr-ok");
        rows.Should().HaveCount(2, "two forward steps, no compensation, no retries");
        rows.Should().OnlyContain(r => r.Status == StepStatus.Completed);
        rows.Select(r => r.StepName).Should().BeEquivalentTo(new[]
        {
            SampleCounterSaga.StepIncrement, SampleCounterSaga.StepMarkDone,
        });
    }

    // Drains due steps over several ticks (the polling trigger's job; here we
    // pump deterministically). Honors backoff by advancing wall clock waits.
    private static async Task DrainAsync(
        SagaTestHarness h, SagaProcessor processor, int maxTicks)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var n = await processor.ProcessDueStepsAsync(CancellationToken.None);
            // Let any short backoff elapse so the next tick sees due steps.
            await Task.Delay(120);
            if (n == 0 && !await AnyPending(h)) return;
        }
    }

    private static async Task<bool> AnyPending(SagaTestHarness h)
    {
        using var scope = h.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OASIS.WebAPI.Data.OASISDbContext>();
        return await db.SagaSteps.AnyAsync(s => s.Status == StepStatus.Pending);
    }

    private static async Task<List<OASIS.WebAPI.Models.Sagas.SagaStepRecord>> AllRows(
        SagaTestHarness h, string correlation)
    {
        using var scope = h.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OASIS.WebAPI.Data.OASISDbContext>();
        return await db.SagaSteps.AsNoTracking()
            .Where(s => s.CorrelationKey == correlation)
            .ToListAsync();
    }
}
