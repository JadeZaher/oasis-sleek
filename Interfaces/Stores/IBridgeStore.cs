using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Bridge;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.Stores;

/// <summary>
/// Persistence boundary for cross-chain bridge transactions and the
/// replay-protection ledger. This is the exactly-once primitive: the
/// conditional transition methods return the affected-row count VERBATIM and
/// the store NEVER asserts==1, retries, or read-modify-writes. All status
/// policy (what a 0 vs 1 row count means) stays in the caller.
/// </summary>
public interface IBridgeStore
{
    /// <summary>Loads a single bridge transaction by id, or null if absent.</summary>
    Task<BridgeTransactionResult?> GetBridgeAsync(string id, CancellationToken ct = default);

    /// <summary>Loads an avatar's bridge transaction history.</summary>
    Task<IReadOnlyList<BridgeTransactionResult>> GetBridgeHistoryAsync(Guid avatarId, CancellationToken ct = default);

    /// <summary>Loads ids of non-terminal bridges last touched before <paramref name="staleBefore"/>, capped at <paramref name="batch"/>.</summary>
    Task<IReadOnlyList<string>> GetNonTerminalBridgeIdsAsync(IReadOnlyCollection<BridgeStatus> nonTerminal, DateTime staleBefore, int batch, CancellationToken ct = default);

    /// <summary>Loads ids of non-terminal operations last touched before <paramref name="staleBefore"/>, capped at <paramref name="batch"/>.</summary>
    Task<IReadOnlyList<Guid>> GetNonTerminalOperationIdsAsync(IReadOnlyCollection<string> nonTerminal, DateTime staleBefore, int batch, CancellationToken ct = default);

    /// <summary>Loads a single blockchain operation by id, or null if absent.</summary>
    Task<BlockchainOperation?> GetOperationAsync(Guid id, CancellationToken ct = default);

    /// <summary>Inserts a new bridge transaction.</summary>
    Task AddBridgeAsync(BridgeTransactionResult tx, CancellationToken ct = default);

    /// <summary>
    /// Attempts to record a consumed VAA. Returns false iff the
    /// UNIQUE(Digest) constraint rejected the insert — that is a detected
    /// replay; true means this VAA was recorded for the first time.
    /// </summary>
    Task<bool> TryInsertConsumedVaaAsync(ConsumedVaaRecord record, CancellationToken ct = default);

    /// <summary>Persists a fetched VAA's bytes/signature-count/proof and advances status to <paramref name="statusVAAReady"/>.</summary>
    Task SaveVaaFetchResultAsync(string id, string vaaBytes, int sigCount, string proofData, BridgeStatus statusVAAReady, CancellationToken ct = default);

    /// <summary>
    /// Conditional status transition: UPDATE … WHERE Id=id AND Status=expected
    /// SET Status=next plus any non-null <paramref name="alsoSet"/> fields.
    /// Returns the affected-row count VERBATIM (0 = lost the race / wrong
    /// state; 1 = won). The store never asserts==1, retries, or RMW.
    /// </summary>
    Task<int> TryTransitionBridgeStatusAsync(string id, BridgeStatus expected, BridgeStatus next, BridgeStatusMutation? alsoSet, CancellationToken ct = default);

    /// <summary>
    /// Conditional operation status transition: UPDATE … WHERE Id=id AND
    /// Status=expected SET Status=next (and CompletedDate when supplied).
    /// Returns the affected-row count VERBATIM. The store never
    /// asserts==1, retries, or RMW.
    /// </summary>
    Task<int> TryTransitionOperationStatusAsync(Guid id, string expected, string next, DateTime? completedDate, CancellationToken ct = default);
}
