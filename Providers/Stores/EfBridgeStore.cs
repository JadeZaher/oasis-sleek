using Microsoft.EntityFrameworkCore;
using OASIS.WebAPI.Data;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Bridge;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Providers.Stores;

/// <summary>
/// EF-backed <see cref="IBridgeStore"/> — a THIN PASS-THROUGH over
/// <see cref="OASISDbContext"/>. This is the exactly-once primitive:
/// <see cref="TryTransitionBridgeStatusAsync"/> and
/// <see cref="TryTransitionOperationStatusAsync"/> issue a single conditional
/// <c>ExecuteUpdateAsync</c> and return the affected-row count VERBATIM. The
/// store NEVER asserts==1, retries, read-modify-writes, or auto-advances any
/// status (including Reversing) — all status policy stays in the caller.
///
/// <para>NOTE: no production code consumes this store yet
/// (<c>CrossChainBridgeService</c>/<c>ReconciliationService</c> keep their
/// direct <c>OASISDbContext _db</c> per the Mission-B spec). It is authored
/// here, behaviorally equivalent, as the SurrealDB-track precondition.</para>
/// </summary>
public sealed class EfBridgeStore : IBridgeStore
{
    private readonly OASISDbContext _db;

    public EfBridgeStore(OASISDbContext db)
    {
        _db = db;
    }

    public async Task<BridgeTransactionResult?> GetBridgeAsync(string id, CancellationToken ct = default)
    {
        return await _db.BridgeTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task<IReadOnlyList<BridgeTransactionResult>> GetBridgeHistoryAsync(Guid avatarId, CancellationToken ct = default)
    {
        return await _db.BridgeTransactions
            .AsNoTracking()
            .Where(b => b.AvatarId == avatarId)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync(ct);
    }

    // Mirrors ReconciliationService.ReconcileBridgeAsync candidate query:
    // AsNoTracking, Where nonTerminal.Contains(Status) && CreatedAt < staleBefore,
    // OrderBy CreatedAt, Take(batch), Select Id.
    public async Task<IReadOnlyList<string>> GetNonTerminalBridgeIdsAsync(
        IReadOnlyCollection<BridgeStatus> nonTerminal, DateTime staleBefore, int batch, CancellationToken ct = default)
    {
        return await _db.BridgeTransactions
            .AsNoTracking()
            .Where(b => nonTerminal.Contains(b.Status) && b.CreatedAt < staleBefore)
            .OrderBy(b => b.CreatedAt)
            .Take(batch)
            .Select(b => b.Id)
            .ToListAsync(ct);
    }

    // Mirrors ReconciliationService.ReconcileOperationsAsync candidate query:
    // AsNoTracking, Where nonTerminal.Contains(Status) && CreatedDate < staleBefore,
    // OrderBy CreatedDate, Take(batch), Select Id.
    public async Task<IReadOnlyList<Guid>> GetNonTerminalOperationIdsAsync(
        IReadOnlyCollection<string> nonTerminal, DateTime staleBefore, int batch, CancellationToken ct = default)
    {
        return await _db.BlockchainOperations
            .AsNoTracking()
            .Where(o => nonTerminal.Contains(o.Status) && o.CreatedDate < staleBefore)
            .OrderBy(o => o.CreatedDate)
            .Take(batch)
            .Select(o => o.Id)
            .ToListAsync(ct);
    }

    public async Task<BlockchainOperation?> GetOperationAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.BlockchainOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    public async Task AddBridgeAsync(BridgeTransactionResult tx, CancellationToken ct = default)
    {
        _db.BridgeTransactions.Add(tx);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Positively-typed unique-violation detection lifted from
    /// <c>CrossChainBridgeService.cs:223-257</c>: insert-then-catch
    /// <see cref="DbUpdateException"/>, detach the conflicting tracked entry,
    /// then re-read by Digest; an existing row ⇒ this is a detected replay,
    /// return false. A genuine (non-duplicate) DB error rethrows.
    /// </summary>
    public async Task<bool> TryInsertConsumedVaaAsync(ConsumedVaaRecord record, CancellationToken ct = default)
    {
        _db.ConsumedVaas.Add(record);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            var dupEntry = _db.ChangeTracker.Entries<ConsumedVaaRecord>()
                .FirstOrDefault(e => e.Entity.Digest == record.Digest);
            if (dupEntry != null)
                dupEntry.State = EntityState.Detached;

            var dup = await _db.ConsumedVaas.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Digest == record.Digest, ct);
            if (dup is not null)
                return false; // duplicate Digest — detected replay

            throw; // genuine DB error, not a replay
        }
    }

    public async Task SaveVaaFetchResultAsync(
        string id, string vaaBytes, int sigCount, string proofData, BridgeStatus statusVAAReady, CancellationToken ct = default)
    {
        await _db.BridgeTransactions
            .Where(b => b.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.VaaBytes, vaaBytes)
                .SetProperty(b => b.VaaSignatureCount, sigCount)
                .SetProperty(b => b.ProofData, proofData)
                .SetProperty(b => b.Status, statusVAAReady), ct);
    }

    /// <summary>
    /// Single conditional UPDATE … WHERE Id==id AND Status==expected SET
    /// Status=next plus a <c>.SetProperty</c> per non-null
    /// <paramref name="alsoSet"/> field. <c>ExecuteUpdateAsync</c> takes ONE
    /// expression tree (a statement body cannot be an
    /// <c>Expression&lt;Func&gt;</c>), so an absent mutation field writes the
    /// column's own current value IN-SQL — a no-op for that column, never an
    /// application-side row read. <c>SetCompletedAtUtcNow</c> ⇒
    /// CompletedAt=UtcNow; <c>ClearCompletedAt</c> ⇒ CompletedAt=null.
    /// Returns the affected-row count VERBATIM (0 = lost the race / wrong
    /// state; 1 = won). No assert, no retry, no read-modify-write, no
    /// auto-advance of any status.
    /// </summary>
    public async Task<int> TryTransitionBridgeStatusAsync(
        string id, BridgeStatus expected, BridgeStatus next, BridgeStatusMutation? alsoSet, CancellationToken ct = default)
    {
        var m = alsoSet;
        return await _db.BridgeTransactions
            .Where(b => b.Id == id && b.Status == expected)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, next)
                .SetProperty(b => b.IdempotencyKey,
                    b => m != null && m.IdempotencyKey != null ? m.IdempotencyKey : b.IdempotencyKey)
                .SetProperty(b => b.ErrorMessage,
                    b => m != null && m.ErrorMessage != null ? m.ErrorMessage : b.ErrorMessage)
                .SetProperty(b => b.LockTxHash,
                    b => m != null && m.LockTxHash != null ? m.LockTxHash : b.LockTxHash)
                .SetProperty(b => b.SourceAddress,
                    b => m != null && m.SourceAddress != null ? m.SourceAddress : b.SourceAddress)
                .SetProperty(b => b.RedemptionTxHash,
                    b => m != null && m.RedemptionTxHash != null ? m.RedemptionTxHash : b.RedemptionTxHash)
                .SetProperty(b => b.MintTxHash,
                    b => m != null && m.MintTxHash != null ? m.MintTxHash : b.MintTxHash)
                .SetProperty(b => b.TargetTokenId,
                    b => m != null && m.TargetTokenId != null ? m.TargetTokenId : b.TargetTokenId)
                .SetProperty(b => b.WormholeEmitterChainId,
                    b => m != null && m.WormholeEmitterChainId != null ? m.WormholeEmitterChainId : b.WormholeEmitterChainId)
                .SetProperty(b => b.WormholeEmitterAddress,
                    b => m != null && m.WormholeEmitterAddress != null ? m.WormholeEmitterAddress : b.WormholeEmitterAddress)
                .SetProperty(b => b.WormholeSequence,
                    b => m != null && m.WormholeSequence != null ? m.WormholeSequence : b.WormholeSequence)
                .SetProperty(b => b.CompletedAt,
                    b => m != null && m.SetCompletedAtUtcNow
                        ? DateTime.UtcNow
                        : (m != null && m.ClearCompletedAt ? (DateTime?)null : b.CompletedAt)),
                ct);
    }

    /// <summary>
    /// Single conditional UPDATE … WHERE Id==id AND Status==expected SET
    /// Status=next (CompletedDate keeps its current value when no
    /// <paramref name="completedDate"/> is supplied). The store stays
    /// string-agnostic about operation status (the caller passes the
    /// <c>OperationStatus</c> constants). Returns the affected-row count
    /// VERBATIM. No assert, no retry, no read-modify-write.
    /// </summary>
    public async Task<int> TryTransitionOperationStatusAsync(
        Guid id, string expected, string next, DateTime? completedDate, CancellationToken ct = default)
    {
        return await _db.BlockchainOperations
            .Where(o => o.Id == id && o.Status == expected)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, next)
                .SetProperty(o => o.CompletedDate,
                    o => completedDate != null ? completedDate : o.CompletedDate),
                ct);
    }
}
