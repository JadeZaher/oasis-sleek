using Microsoft.EntityFrameworkCore;
using Npgsql;
using OASIS.WebAPI.Data;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Models.Idempotency;

namespace OASIS.WebAPI.Core.Idempotency;

/// <summary>
/// EF Core-backed <see cref="IIdempotencyStore"/>.
///
/// Atomicity model: the <c>IdempotencyRecords</c> table has a UNIQUE constraint
/// on <see cref="IdempotencyRecord.Key"/> (configured in
/// <see cref="OASISDbContext"/>). <see cref="TryClaimAsync"/> attempts to INSERT
/// a fresh <see cref="IdempotencyState.InProgress"/> row. Exactly one concurrent
/// caller's INSERT succeeds (it "wins" the claim). Every other concurrent or
/// later caller's INSERT raises the database UNIQUE-constraint violation; that
/// path is POSITIVELY identified (PostgreSQL SQLSTATE 23505, or the SQLite
/// equivalent under test), then re-reads the now-committed winning row and
/// returns it with <see cref="IdempotencyClaim.Won"/> == <c>false</c>. ANY
/// other database error is rethrown unchanged — a genuine failure must never be
/// silently misreported as an idempotent replay.
///
/// Isolation: this store is registered <c>Scoped</c> but it MUST NOT flush the
/// caller's request-scoped <see cref="OASISDbContext"/> (which is shared with
/// <c>CrossChainBridgeService</c> / <c>BlockchainOperationManager</c> and may
/// hold unrelated tracked entities). Every operation therefore creates its OWN
/// short-lived DI scope and resolves a dedicated <see cref="OASISDbContext"/>
/// from it, so the claim INSERT/UPDATE is never batched with unrelated tracked
/// modifications. This mirrors the established pattern in
/// <c>Core/AlgorandFaucet.cs</c>.
/// </summary>
public sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly IServiceScopeFactory _scopeFactory;

    public IdempotencyStore(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task<IdempotencyClaim> TryClaimAsync(string key, string operationType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Idempotency key must be non-empty.", nameof(key));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OASISDbContext>();

        // Fast path: if a record already exists, return it without attempting
        // an insert. This is a cheap, idempotent existence check whose entire
        // purpose is to resolve a duplicate request to its prior outcome; it
        // runs with CancellationToken.None ON PURPOSE. Honouring a cancelled
        // request token here would defeat exactly-once (a cancelled DUPLICATE
        // must still replay, never surface a raw cancellation/DB error). The
        // meaningful cancellation point is BEFORE the irreversible effect,
        // which the caller controls after TryClaimAsync returns.
        var existing = await db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == key, CancellationToken.None);
        if (existing is not null)
            return new IdempotencyClaim(false, existing);

        var now = DateTime.UtcNow;
        var record = new IdempotencyRecord
        {
            Key = key,
            OperationType = operationType,
            State = IdempotencyState.InProgress,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.IdempotencyRecords.Add(record);

        try
        {
            // This scope's context holds ONLY this record — SaveChanges flushes
            // exactly the claim INSERT, never an unrelated caller entity.
            await db.SaveChangesAsync(ct);
            // This caller's INSERT succeeded — it owns the claim.
            return new IdempotencyClaim(true, record);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // POSITIVELY identified UNIQUE-key violation: another caller won the
            // race (or this is a duplicate request). Re-read the committed
            // winning row and return it. This holds regardless of cancellation
            // — a duplicate request must always resolve to the idempotent
            // replay, never surface the raw DB error.
            var winner = await db.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Key == key, CancellationToken.None);

            if (winner is not null)
                return new IdempotencyClaim(false, winner);

            // A UNIQUE violation but the winning row vanished (e.g. concurrent
            // delete) — surface the original error rather than fabricate a claim.
            throw;
        }
        // Any other DbUpdateException (inner is not a 23505 / SQLite unique
        // violation) is a GENUINE error and propagates unchanged — it must
        // never be masked as an idempotent replay.
    }

    /// <inheritdoc />
    public async Task CompleteAsync(string key, string resultPayload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Idempotency key must be non-empty.", nameof(key));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OASISDbContext>();

        var record = await db.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key == key, ct)
            ?? throw new InvalidOperationException(
                $"Cannot complete idempotency key '{key}': no claim exists. " +
                "CompleteAsync must follow a winning TryClaimAsync.");

        record.State = IdempotencyState.Completed;
        record.ResultPayload = resultPayload;
        record.Error = null;
        record.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task FailAsync(string key, string error, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Idempotency key must be non-empty.", nameof(key));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OASISDbContext>();

        var record = await db.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key == key, ct)
            ?? throw new InvalidOperationException(
                $"Cannot fail idempotency key '{key}': no claim exists. " +
                "FailAsync must follow a winning TryClaimAsync.");

        record.State = IdempotencyState.Failed;
        record.Error = error;
        record.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IdempotencyRecord?> GetAsync(string key, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OASISDbContext>();

        return await db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == key, ct);
    }

    // SQLite extended result codes (Microsoft.Data.Sqlite surfaces these via
    // SqliteException.SqliteExtendedErrorCode). The bare primary code 19
    // (SQLITE_CONSTRAINT) is INTENTIONALLY NOT accepted: it also covers NOT
    // NULL / CHECK / FOREIGN KEY failures, so matching it would re-introduce
    // exactly the misclassification HIGH-1 is about (a genuine non-unique
    // error masked as an idempotent replay). Only the two extended codes that
    // mean "a row with this key already exists" are treated as the win-race:
    //   2067 = SQLITE_CONSTRAINT_UNIQUE     (duplicate on a UNIQUE index)
    //   1555 = SQLITE_CONSTRAINT_PRIMARYKEY (duplicate on the PRIMARY KEY)
    // IdempotencyRecord.Key is the PRIMARY KEY (OASISDbContext.OnModelCreating:
    // HasKey(e => e.Key) + a redundant unique index), so under SQLite a
    // duplicate insert surfaces as 1555; under a plain unique index it is 2067.
    private const int SqliteConstraintUnique = 2067;
    private const int SqliteConstraintPrimaryKey = 1555;

    /// <summary>
    /// Positively identifies a database UNIQUE / PRIMARY-KEY constraint
    /// violation (i.e. "another caller already inserted this key") by walking
    /// the inner-exception chain. Recognises BOTH providers reachable here:
    /// PostgreSQL (production) via <see cref="PostgresException"/> SQLSTATE
    /// <c>23505</c> (<see cref="PostgresErrorCodes.UniqueViolation"/>), and
    /// SQLite (the relational test backend) via <see cref="SqliteException"/>
    /// extended code <c>2067</c> (SQLITE_CONSTRAINT_UNIQUE) or <c>1555</c>
    /// (SQLITE_CONSTRAINT_PRIMARYKEY). Any other error (NOT NULL, CHECK, FK,
    /// connection, …) returns <c>false</c> so it is rethrown rather than
    /// misclassified as an idempotent replay.
    /// </summary>
    private static bool IsUniqueViolation(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
                return true;

            var t = e.GetType();
            if (t.FullName == "Microsoft.Data.Sqlite.SqliteException")
            {
                var ext = t.GetProperty("SqliteExtendedErrorCode")?.GetValue(e) as int?;
                if (ext == SqliteConstraintUnique || ext == SqliteConstraintPrimaryKey)
                    return true;
            }
        }

        return false;
    }
}
