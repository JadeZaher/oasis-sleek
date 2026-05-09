namespace OASIS.WebAPI.Core.Blockchain.Wormhole;

/// <summary>
/// Bridge execution mode — determines whether cross-chain transfers
/// use trustless on-chain verification or the OASIS custodial orchestrator.
/// </summary>
public enum BridgeMode
{
    /// <summary>OASIS server coordinates lock→mint. Fast but centralized.</summary>
    Trusted,

    /// <summary>Wormhole Guardian network produces VAAs for on-chain proof verification.</summary>
    Wormhole
}

/// <summary>
/// Configuration for the Wormhole bridge adapter.
/// Bind from appsettings under "Blockchain:Wormhole".
/// </summary>
public class WormholeConfig
{
    public const string SectionName = "Blockchain:Wormhole";

    /// <summary>Wormhole Guardian RPC / REST API base URL.</summary>
    public string GuardianRpcUrl { get; set; } = "https://wormhole-v2-mainnet-api.certus.one";

    /// <summary>Wormhole chain IDs used to map OASIS chain names to Wormhole emitters.</summary>
    public Dictionary<string, WormholeChainMapping> ChainMappings { get; set; } = new()
    {
        ["Solana"] = new() { WormholeChainId = 1, CoreBridgeAddress = "worm2ZoG2kUd4vFXhvjh93UUH596ayRfgQ2MgjNMTth" },
        ["Algorand"] = new() { WormholeChainId = 8, CoreBridgeAddress = "842125965" }
    };

    /// <summary>Default bridge mode for new bridge requests.</summary>
    public BridgeMode DefaultMode { get; set; } = BridgeMode.Wormhole;

    /// <summary>Max time to wait for Guardian signatures on a VAA.</summary>
    public int VaaTimeoutSeconds { get; set; } = 120;

    /// <summary>Polling interval when waiting for VAA availability.</summary>
    public int VaaPollIntervalMs { get; set; } = 2000;

    /// <summary>Minimum Guardian signatures required for VAA acceptance.</summary>
    public int MinGuardianSignatures { get; set; } = 13;
}

/// <summary>
/// Maps an OASIS chain name to its Wormhole chain ID and core bridge contract/program address.
/// </summary>
public class WormholeChainMapping
{
    /// <summary>Wormhole's numeric chain identifier (1=Solana, 8=Algorand, 2=Ethereum, etc.)</summary>
    public int WormholeChainId { get; set; }

    /// <summary>Address of the Wormhole Core Bridge contract/program on this chain.</summary>
    public string CoreBridgeAddress { get; set; } = string.Empty;

    /// <summary>Address of the Wormhole Token Bridge contract/program (for wrapped assets).</summary>
    public string? TokenBridgeAddress { get; set; }

    /// <summary>Address of the Wormhole NFT Bridge contract/program.</summary>
    public string? NftBridgeAddress { get; set; }
}
