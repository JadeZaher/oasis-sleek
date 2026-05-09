using OASIS.WebAPI.Core.Blockchain.Wormhole;

namespace OASIS.WebAPI.Models.Responses;

/// <summary>
/// Represents a cross-chain bridge transaction tracked by the bridge orchestrator.
/// </summary>
public class BridgeTransactionResult
{
    public string Id { get; set; } = string.Empty;
    public Guid AvatarId { get; set; }
    public string SourceChain { get; set; } = string.Empty;
    public string TargetChain { get; set; } = string.Empty;
    public string SourceTokenId { get; set; } = string.Empty;
    public string? TargetTokenId { get; set; }
    public string SourceAddress { get; set; } = string.Empty;
    public string TargetAddress { get; set; } = string.Empty;
    public int Amount { get; set; }
    public BridgeStatus Status { get; set; }
    public BridgeMode Mode { get; set; } = BridgeMode.Trusted;
    public string? LockTxHash { get; set; }
    public string? MintTxHash { get; set; }
    public string? ProofData { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // ─── Wormhole-specific fields (populated when Mode == Wormhole) ───

    /// <summary>Wormhole emitter chain ID for the source chain.</summary>
    public int? WormholeEmitterChainId { get; set; }

    /// <summary>Wormhole emitter address (hex, 32 bytes).</summary>
    public string? WormholeEmitterAddress { get; set; }

    /// <summary>Wormhole message sequence number.</summary>
    public long? WormholeSequence { get; set; }

    /// <summary>Base64-encoded signed VAA (set after Guardian consensus).</summary>
    public string? VaaBytes { get; set; }

    /// <summary>Number of Guardian signatures on the VAA.</summary>
    public int? VaaSignatureCount { get; set; }

    /// <summary>Target-chain redemption tx hash (set after VAA submission).</summary>
    public string? RedemptionTxHash { get; set; }
}

public enum BridgeStatus
{
    Initiated,
    Locked,
    AwaitingVAA,
    VAAReady,
    Redeeming,
    Minted,
    Completed,
    Failed,
    Refunded
}
