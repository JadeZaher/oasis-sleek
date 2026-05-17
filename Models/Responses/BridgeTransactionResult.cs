using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using OASIS.WebAPI.Core.Blockchain.Wormhole;

namespace OASIS.WebAPI.Models.Responses;

/// <summary>
/// Represents a cross-chain bridge transaction, persisted via EF Core.
/// </summary>
[Table("BridgeTransactions")]
public class BridgeTransactionResult
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    public Guid AvatarId { get; set; }

    [MaxLength(32)]
    public string SourceChain { get; set; } = string.Empty;

    [MaxLength(32)]
    public string TargetChain { get; set; } = string.Empty;

    [MaxLength(256)]
    public string SourceTokenId { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? TargetTokenId { get; set; }

    [MaxLength(512)]
    public string SourceAddress { get; set; } = string.Empty;

    [MaxLength(512)]
    public string TargetAddress { get; set; } = string.Empty;

    public int Amount { get; set; }

    public BridgeStatus Status { get; set; }

    public BridgeMode Mode { get; set; } = BridgeMode.Trusted;

    [MaxLength(256)]
    public string? LockTxHash { get; set; }

    [MaxLength(256)]
    public string? MintTxHash { get; set; }

    [MaxLength(2048)]
    public string? ProofData { get; set; }

    [MaxLength(1024)]
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    // ─── Wormhole-specific fields (populated when Mode == Wormhole) ───

    public int? WormholeEmitterChainId { get; set; }

    [MaxLength(128)]
    public string? WormholeEmitterAddress { get; set; }

    public long? WormholeSequence { get; set; }

    [MaxLength(4096)]
    public string? VaaBytes { get; set; }

    public int? VaaSignatureCount { get; set; }

    [MaxLength(256)]
    public string? RedemptionTxHash { get; set; }

    // ─── Exactly-once / atomic-transition safety fields (Wave 1 contract) ───

    /// <summary>
    /// Idempotency key for the irreversible operation that produced/advances
    /// this bridge transaction (e.g., the redeem request's Idempotency-Key).
    /// Nullable for back-compat with rows created before this field existed.
    /// </summary>
    [MaxLength(200)]
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Optimistic-concurrency token. Mapped to PostgreSQL's system column
    /// <c>xmin</c> (row version) via Npgsql — see <c>OASISDbContext</c>. It is
    /// read-only/database-generated; every committed UPDATE bumps it. Wave 2
    /// uses this for atomic conditional state transitions: a stale read causes
    /// <c>SaveChangesAsync</c> to throw <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>,
    /// OR use <c>ExecuteUpdateAsync</c> with a <c>WHERE Status == expected</c>
    /// predicate and assert exactly one row was affected.
    /// </summary>
    public uint Version { get; set; }
}

public enum BridgeStatus
{
    Initiated,
    Locked,
    AwaitingVAA,
    VAAReady,
    Redeeming,
    // NOTE: 'Minted' was removed (Wave 1) — it was dead code. The lifecycle is
    // strictly Initiated→Locked→AwaitingVAA→VAAReady→Redeeming→Completed
    // (Failed/Refunded terminal). No code ever assigned BridgeStatus.Minted.
    Completed,
    Failed,
    Refunded
}
