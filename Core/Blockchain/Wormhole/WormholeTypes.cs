using System.Text.Json.Serialization;

namespace OASIS.WebAPI.Core.Blockchain.Wormhole;

/// <summary>
/// A Verified Action Approval — the cryptographic proof produced by the
/// Wormhole Guardian network attesting that an event occurred on a source chain.
/// </summary>
public class WormholeVAA
{
    /// <summary>VAA version (currently 1).</summary>
    public int Version { get; set; } = 1;

    /// <summary>Index of the Guardian set that signed this VAA.</summary>
    public int GuardianSetIndex { get; set; }

    /// <summary>Number of Guardian signatures on this VAA.</summary>
    public int SignatureCount { get; set; }

    /// <summary>Raw VAA bytes encoded as base64.</summary>
    public string VaaBytes { get; set; } = string.Empty;

    /// <summary>Wormhole chain ID of the source chain.</summary>
    public int EmitterChainId { get; set; }

    /// <summary>Address of the emitting contract on the source chain (hex-encoded, 32 bytes).</summary>
    public string EmitterAddress { get; set; } = string.Empty;

    /// <summary>Monotonically increasing sequence number from the emitter.</summary>
    public long Sequence { get; set; }

    /// <summary>Keccak256 digest of the VAA body.</summary>
    public string? Digest { get; set; }

    /// <summary>When the VAA was observed by the Guardians.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Application-level payload inside the VAA.</summary>
    public string? Payload { get; set; }
}

/// <summary>
/// Result of a Wormhole transfer initiation (source-chain side).
/// Contains the data needed to fetch the VAA from the Guardian network.
/// </summary>
public class WormholeTransferInitiation
{
    public string TxHash { get; set; } = string.Empty;
    public int EmitterChainId { get; set; }
    public string EmitterAddress { get; set; } = string.Empty;
    public long Sequence { get; set; }
}

/// <summary>
/// Result of redeeming (completing) a Wormhole transfer on the target chain.
/// </summary>
public class WormholeRedemptionResult
{
    public string TxHash { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

// ─── Guardian API response DTOs ───

/// <summary>
/// Response from api.wormholescan.io/v1/signed_vaa/{chain}/{emitter}/{seq}.
/// WormholeScan returns vaaBytes at the top level (no data envelope).
/// </summary>
public class GuardianVAAEnvelope
{
    [JsonPropertyName("vaaBytes")]
    public string? VaaBytes { get; set; }

    [JsonPropertyName("vaa")]
    public string? Vaa { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}
