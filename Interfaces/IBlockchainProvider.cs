using OASIS.WebAPI.Core;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces;

public interface IBlockchainProvider
{
    string ChainType { get; }
    ChainNetwork ActiveNetwork { get; }

    void Initialize(BlockchainNetworkConfig config, ChainNetwork network);

    /// <summary>
    /// Get a chain-specific capability module (e.g., IAlgorandASAModule, ISolanaMetaplexModule).
    /// </summary>
    bool TryGetModule<T>(out T? module) where T : class, IBlockchainProviderModule;

    // ─── Account / Wallet ───
    Task<OASISResult<string>> GetBalanceAsync(string address, string? tokenId = null, CancellationToken ct = default);
    Task<OASISResult<bool>> ValidateAddressAsync(string address, CancellationToken ct = default);

    // ─── Token / Asset Lifecycle ───
    Task<OASISResult<string>> MintAsync(
        string tokenUri,
        int amount,
        string assetType,
        string walletAddress,
        CancellationToken ct = default);

    Task<OASISResult<string>> BurnAsync(
        string tokenId,
        int amount,
        string walletAddress,
        CancellationToken ct = default);

    Task<OASISResult<string>> TransferAsync(
        string tokenId,
        string fromAddress,
        string toAddress,
        int amount,
        CancellationToken ct = default);

    // ─── Exchange / Swap ───
    Task<OASISResult<string>> ExchangeAsync(
        string sourceTokenId,
        string targetTokenId,
        string exchangeRate,
        string walletAddress,
        CancellationToken ct = default);

    Task<OASISResult<string>> SwapAsync(
        string tokenIn,
        string tokenOut,
        decimal amountIn,
        decimal minAmountOut,
        string walletAddress,
        CancellationToken ct = default);

    // ─── Query / Metadata ───
    Task<OASISResult<Dictionary<string, object>>> GetTokenMetadataAsync(
        string tokenId,
        CancellationToken ct = default);

    Task<OASISResult<List<Dictionary<string, object>>>> GetTokensByOwnerAsync(
        string ownerAddress,
        CancellationToken ct = default);

    Task<OASISResult<Dictionary<string, object>>> GetTransactionStatusAsync(
        string txHash,
        CancellationToken ct = default);

    // ─── Smart Contract / Program ───
    Task<OASISResult<string>> DeployContractAsync(
        byte[] contractCode,
        string walletAddress,
        Dictionary<string, object>? args = null,
        CancellationToken ct = default);

    Task<OASISResult<object>> CallContractAsync(
        string contractAddress,
        string method,
        Dictionary<string, object> args,
        string walletAddress,
        CancellationToken ct = default);

    // ─── Chain Info ───
    Task<OASISResult<Dictionary<string, object>>> GetChainInfoAsync(CancellationToken ct = default);

    // ─── Cross-Chain Bridge Primitives ───
    /// <summary>
    /// Lock an asset in a bridge vault on this chain (for outbound bridging).
    /// </summary>
    Task<OASISResult<string>> LockForBridgeAsync(
        string tokenId, string vaultAddress, int amount,
        string targetChain, string targetRecipient, CancellationToken ct = default);

    /// <summary>
    /// Mint a wrapped asset representation of an asset from another chain.
    /// </summary>
    Task<OASISResult<string>> MintWrappedAsync(
        string sourceChain, string sourceTokenId, string tokenUri,
        int amount, string recipientAddress, CancellationToken ct = default);

    /// <summary>
    /// Burn a wrapped asset to release the original asset on the source chain.
    /// </summary>
    Task<OASISResult<string>> BurnWrappedAsync(
        string tokenId, int amount, string sourceChain,
        string sourceRecipient, string walletAddress, CancellationToken ct = default);

    /// <summary>
    /// Verify a cross-chain proof/message from another chain.
    /// </summary>
    Task<OASISResult<bool>> VerifyBridgeProofAsync(
        string proofData, string sourceChain, string targetChainId, CancellationToken ct = default);

    /// <summary>
    /// Check if this provider supports bridging operations natively.
    /// </summary>
    bool SupportsBridging { get; }
}
