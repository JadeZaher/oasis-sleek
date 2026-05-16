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

    /// <summary>WormholeScan Guardian REST API base URL.</summary>
    public string GuardianRpcUrl { get; set; } = "https://api.wormholescan.io";

    /// <summary>Wormhole chain IDs used to map OASIS chain names to Wormhole emitters.
    /// Addresses are mainnet. For devnet/testnet overrides, see ChainMappingsDevnet.</summary>
    public Dictionary<string, WormholeChainMapping> ChainMappings { get; set; } = new()
    {
        ["Solana"] = new()
        {
            WormholeChainId = 1,
            CoreBridgeAddress = "worm2ZoG2kUd4vFXhvjh93UUH596ayRfgQ2MgjNMTth",
            TokenBridgeAddress = "wormDTUJ6AWPNvk59vGQbDvGJmqbDTdgWgAqcL6gibu"
        },
        ["Algorand"] = new()
        {
            WormholeChainId = 8,
            CoreBridgeAddress = "842125965",
            TokenBridgeAddress = "1088520416"
        },
        ["Ethereum"] = new()
        {
            WormholeChainId = 2,
            CoreBridgeAddress = "0x98f3c9e6E3fAce36bAAd05FE09d375Ef1464288B",
            TokenBridgeAddress = "0x3ee18B2214AFF97000D974cf647E7C347E8fa585"
        }
    };

    /// <summary>Devnet/testnet overrides per chain. When in devnet mode and a mapping exists here, it takes precedence.</summary>
    public Dictionary<string, WormholeChainMapping> ChainMappingsDevnet { get; set; } = new()
    {
        ["Solana"] = new()
        {
            WormholeChainId = 1,
            CoreBridgeAddress = "3u8hJUVTA4jH1wYAyUpm7MKgCtsK7ZrqWmMNEhQ5Y8Ru",
            TokenBridgeAddress = "DZnkkTmCiFWfYTfT41X3Rd1kDgozqzxWaHqsw6W4x2oe"
        },
        ["Algorand"] = new()
        {
            WormholeChainId = 8,
            CoreBridgeAddress = "634108368",
            TokenBridgeAddress = "634108369"
        }
    };

    /// <summary>Bridge vault addresses per chain (trusted mode). The server uses these as lock/deposit addresses.</summary>
    public Dictionary<string, BridgeVaultConfig> BridgeVaults { get; set; } = new()
    {
        ["Solana"] = new() { VaultAddress = "", Index = 0 },
        ["Algorand"] = new() { VaultAddress = "", Index = 0 },
        ["Ethereum"] = new() { VaultAddress = "", Index = 0 }
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

/// <summary>
/// Configuration for a bridge vault on a specific chain (trusted mode).
/// </summary>
public class BridgeVaultConfig
{
    public string VaultAddress { get; set; } = string.Empty;
    public int Index { get; set; }
}
