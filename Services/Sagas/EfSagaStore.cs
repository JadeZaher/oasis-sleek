using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OASIS.WebAPI.Data;
using OASIS.WebAPI.Models.Sagas;

namespace OASIS.WebAPI.Sagas;

/// <summary>
/// EF Core-backed <see cref="ISagaStore"/>. All durable specifics live here,
/// behind the seam. Every mutating operation is an atomic CONDITIONAL update
/// (<c>ExecuteUpdateAsync … WHERE Id==id AND Status==&lt;expected&gt;</c> +
/// assert exactly one row) — the same single-winner discipline proven in
/// <c>ReconciliationService</c> / <c>IdempotencyStore</c>. The conditional
/// predicate (NOT the optimistic-concurrency exception) is the arbiter, so the
/// guarantee holds identically on PostgreSQL (production) and SQLite (tests).
///
/// <para><b>Scope isolation.</b> Like <c>IdempotencyStore</c>, every call
/// creates its OWN short-lived DI scope and resolves a dedicated
/// <see cref="OASISDbContext"/> so a saga write is never batched with an
/// unrelated caller's tracked entities. The processor (a hosted singleton) is
/// safe to share this scoped-resolving store.</para>
/// </summary>
public sealed class EfSagaStore : ISagaStore
{
    private readonly IServiceScopeFactory _scopeFactory;

    public EfSagaStore(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<SagaStepRecord> EnqueueAsync(
        string sagaName,
        string stepName,
        string correlationKey,
        string stepIdempotencyKey,
        string payloadJson,
        bool isCompensation,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OASISDbContext>();

        var now = DateTime.UtcNow;
        var record = new SagaStepRecord
        {
            Id = Guid.NewGuid(),
            SagaName = sagaName,
            StepName = stepName,
            CorrelationKey = correlationKey,
            StepIdempotencyKey = stepIdempotencyKey,
            Payload = payloadJson,
            Status = StepStatus.Pending,
            IsCompensation = isCompensation,
            AttemptCount = 0,
            NextRunAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.SagaSteps.Add(record);
        await db.SaveChangesAsync(ct);
        return record;
    }

    public Task<SagaStepRecord> EnqueueNextStepAsync(
        string sagaName,
        string nextStepName,
        string correlationKey,
        string stepIdempotencyKey,
        string payloadJson,
        CancellationToken ct)
        => EnqueueAsync(sagaName, nextStepName, correlationKey,
            stepIdempotencyKey, payloadJson, isCompensation: false, ct);

    public async Task<IReadOnlyList<Guid>> GetDueStepIdsAsync(
        DateTime now, int batch, TimeSpan leaseTimeout, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OASISDbContext>();

        // Lease/visibility-timeout reclaim: an InProgress row whose ClaimedAt is
        // older than the lease belongs to a processor that died mid-step. Return
        // it to Pending+due (NextRunAt=now) so it is reclaimed and resumed. This
        // is a conditional bulk UPDATE (no row mutated unless it truly lapsed) —
        // crash-safe re-entry. The handler keying its effect on the stable
        // StepIdempotencyKey means a resumed step is an idempotent replay, never
        // a double-run.
        var leaseCutoff = now - leaseTimeout;
        await db.SagaSteps
            .Where(s => s.Status == StepStatus.InProgress
                        && s.ClaimedAt != null
                        && s.ClaimedAt < leaseCutoff)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Status, StepStatus.Pending)
                .SetProperty(s => s.NextRunAt, now)
                .SetProperty(s => s.ClaimedAt, (DateTime?)null)
                .SetProperty(s => s.UpdatedAt, now),
                ct);

        var safeBatch = Math.Clamp(batch, 1, 1000);
        return await db.SagaSteps
            .AsNoTracking()
            .Where(s => s.Status == StepStatus.Pending && s.NextRunAt <= now)
            .OrderBy(s => s.NextRunAt)
            .Take(safeBatch)
            .Select(s => s.Id)
            .ToListAsync(ct);
    }

    public async Task<SagaStepRecord?> TryClaimDueStepAsync(
        Guid id, DateTime now, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OASISDbContext>();

        // THE single-winner primitive. Atomic conditional UPDATE; the predicate
        // (Id + Status==Pending + still due) is the arbiter. Under N racing
        // processors at most one ExecuteUpdateAsync affects a row — the others
        // see affected==0 and back off (return null). Mirrors
        // ReconciliationService's ExecuteUpdateAsync … WHERE Status==expected.
        int affected = await db.SagaSteps
            .Where(s => s.Id == id
                        && s.Status == StepStatus.Pending
                        && s.NextRunAt <= now)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Status, StepStatus.InProgress)
                .SetProperty(s => s.ClaimedAt, now)
                .SetProperty(s => s.UpdatedAt, now),
                ct);

        if (affected != 1)
            return null; // lost the race / no longer due — single winner only

        return await db.SagaSteps.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<bool> CompleteStepAsync(Guid id, string? output, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OASISDbContext>();

        var now = DateTime.UtcNow;
        int affected = await db.SagaSteps
            .Where(s => s.Id == id && s.Status == StepStatus.InProgress)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Status, StepStatus.Completed)
                .SetProperty(s => s.Output, output)
                .SetProperty(s => s.ClaimedAt, (DateTime?)null)
                .SetProperty(s => s.UpdatedAt, now),
                ct);

        return affected == 1;
    }

    public async Task<bool> ScheduleRetryAsync(
        Guid id, DateTime nextRunAt, string error, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OASISDbContext>();

        var now = DateTime.UtcNow;
        int affected = await db.SagaSteps
            .Where(s => s.Id == id && s.Status == StepStatus.InProgress)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Status, StepStatus.Pending)
                .SetProperty(s => s.AttemptCount, s => s.AttemptCount + 1)
                .SetProperty(s => s.NextRunAt, nextRunAt)
                .SetProperty(s => s.ClaimedAt, (DateTime?)null)
                .SetProperty(s => s.LastError, Truncate(error, 2048))
                .SetProperty(s => s.UpdatedAt, now),
                ct);

        return affected == 1;
    }

    public async Task<SagaStepRecord?> CompensateStepAsync(
        Guid id,
        string compensationStepName,
        string compensationIdempotencyKey,
        string compensationPayloadJson,
        string error,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OASISDbContext>();

        // Conditionally settle the exhausted forward step to Compensating;
        // only the winner of this transition proceeds to enqueue the
        // compensation row (so a concurrent reclaim cannot double-enqueue it).
        var now = DateTime.UtcNow;
        var failing = await db.SagaSteps.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (failing is null)
            return null;

        int affected = await db.SagaSteps
            .Where(s => s.Id == id && s.Status == StepStatus.InProgress)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Status, StepStatus.Compensating)
                .SetProperty(s => s.AttemptCount, s => s.AttemptCount + 1)
                .SetProperty(s => s.ClaimedAt, (DateTime?)null)
                .SetProperty(s => s.LastError, Truncate(error, 2048))
                .SetProperty(s => s.UpdatedAt, now),
                ct);

        if (affected != 1)
            return null; // a concurrent transition already handled it

        var compensation = new SagaStepRecord
        {
            Id = Guid.NewGuid(),
            SagaName = failing.SagaName,
            StepName = compensationStepName,
            CorrelationKey = failing.CorrelationKey,
            StepIdempotencyKey = compensationIdempotencyKey,
            Payload = compensationPayloadJson,
            Status = StepStatus.Pending,
            IsCompensation = true,
            AttemptCount = 0,
            NextRunAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.SagaSteps.Add(compensation);
        await db.SaveChangesAsync(ct);
        return compensation;
    }

    public async Task<bool> DeadLetterStepAsync(Guid id, string error, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OASISDbContext>();

        var now = DateTime.UtcNow;
        int affected = await db.SagaSteps
            .Where(s => s.Id == id && s.Status == StepStatus.InProgress)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Status, StepStatus.DeadLettered)
                .SetProperty(s => s.DeadLettered, true)
                .SetProperty(s => s.AttemptCount, s => s.AttemptCount + 1)
                .SetProperty(s => s.ClaimedAt, (DateTime?)null)
                .SetProperty(s => s.LastError, Truncate(error, 2048))
                .SetProperty(s => s.UpdatedAt, now),
                ct);

        return affected == 1;
    }

    public async Task<SagaStepRecord?> GetAsync(Guid id, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OASISDbContext>();
        return await db.SagaSteps.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    private static string? Truncate(string? s, int max) =>
        s is null || s.Length <= max ? s : s[..max];
}
