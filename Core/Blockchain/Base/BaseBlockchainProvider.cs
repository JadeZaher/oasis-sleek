using OASIS.WebAPI.Core;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Providers.Blockchain.Base;

/// <summary>
/// Base class for blockchain providers with shared retry, error handling, and configuration logic.
/// All IBlockchainProvider methods are virtual so derived providers can override them.
/// </summary>
public abstract class BaseBlockchainProvider : IBlockchainProvider
{
    protected readonly IConfiguration _config;
    protected readonly ILogger _logger;
    protected BlockchainNetworkConfig _networkConfig = new();

    public abstract string ChainType { get; }
    public ChainNetwork ActiveNetwork { get; protected set; }

    protected BaseBlockchainProvider(IConfiguration config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public virtual void Initialize(BlockchainNetworkConfig config, ChainNetwork network)
    {
        _networkConfig = config;
        ActiveNetwork = network;
        _logger.LogInformation(
            "Initialized {ChainType} provider on {Network} with node {NodeUrl}",
            ChainType, network, config.NodeUrl);
    }

    public bool TryGetModule<T>(out T? module) where T : class, IBlockchainProviderModule
    {
        if (this is T t)
        {
            module = t;
            return true;
        }
        module = null;
        return false;
    }

    // ─── IBlockchainProvider virtual implementations (override in derived) ───

    public virtual Task<OASISResult<string>> GetBalanceAsync(
        string address, string? tokenId = null, CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>($"{ChainType} GetBalanceAsync not implemented"));
    }

    public virtual Task<OASISResult<bool>> ValidateAddressAsync(
        string address, CancellationToken ct = default)
    {
        return Task.FromResult(new OASISResult<bool>
        {
            IsError = true,
            Result = false,
            Message = $"{ChainType} ValidateAddressAsync not implemented"
        });
    }

    public virtual Task<OASISResult<string>> MintAsync(
        string tokenUri, int amount, string assetType, string walletAddress,
        CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>($"{ChainType} MintAsync not implemented"));
    }

    public virtual Task<OASISResult<string>> BurnAsync(
        string tokenId, int amount, string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>($"{ChainType} BurnAsync not implemented"));
    }

    public virtual Task<OASISResult<string>> TransferAsync(
        string tokenId, string fromAddress, string toAddress, int amount,
        CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>($"{ChainType} TransferAsync not implemented"));
    }

    public virtual Task<OASISResult<string>> ExchangeAsync(
        string sourceTokenId, string targetTokenId, string exchangeRate,
        string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>($"{ChainType} ExchangeAsync not implemented"));
    }

    public virtual Task<OASISResult<string>> SwapAsync(
        string tokenIn, string tokenOut, decimal amountIn, decimal minAmountOut,
        string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>($"{ChainType} SwapAsync not implemented"));
    }

    public virtual Task<OASISResult<Dictionary<string, object>>> GetTokenMetadataAsync(
        string tokenId, CancellationToken ct = default)
    {
        return Task.FromResult(Error<Dictionary<string, object>>($"{ChainType} GetTokenMetadataAsync not implemented"));
    }

    public virtual Task<OASISResult<List<Dictionary<string, object>>>> GetTokensByOwnerAsync(
        string ownerAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Error<List<Dictionary<string, object>>>($"{ChainType} GetTokensByOwnerAsync not implemented"));
    }

    public virtual Task<OASISResult<Dictionary<string, object>>> GetTransactionStatusAsync(
        string txHash, CancellationToken ct = default)
    {
        return Task.FromResult(Error<Dictionary<string, object>>($"{ChainType} GetTransactionStatusAsync not implemented"));
    }

    public virtual Task<OASISResult<string>> DeployContractAsync(
        byte[] contractCode, string walletAddress,
        Dictionary<string, object>? args = null, CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>($"{ChainType} DeployContractAsync not implemented"));
    }

    public virtual Task<OASISResult<object>> CallContractAsync(
        string contractAddress, string method, Dictionary<string, object> args,
        string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Error<object>($"{ChainType} CallContractAsync not implemented"));
    }

    public virtual Task<OASISResult<Dictionary<string, object>>> GetChainInfoAsync(
        CancellationToken ct = default)
    {
        return Task.FromResult(Error<Dictionary<string, object>>($"{ChainType} GetChainInfoAsync not implemented"));
    }

    public virtual Task<OASISResult<string>> LockForBridgeAsync(
        string tokenId, string vaultAddress, int amount,
        string targetChain, string targetRecipient, CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>($"{ChainType} LockForBridgeAsync not implemented"));
    }

    public virtual Task<OASISResult<string>> MintWrappedAsync(
        string sourceChain, string sourceTokenId, string tokenUri,
        int amount, string recipientAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>($"{ChainType} MintWrappedAsync not implemented"));
    }

    public virtual Task<OASISResult<string>> BurnWrappedAsync(
        string tokenId, int amount, string sourceChain,
        string sourceRecipient, string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>($"{ChainType} BurnWrappedAsync not implemented"));
    }

    public virtual Task<OASISResult<bool>> VerifyBridgeProofAsync(
        string proofData, string sourceChain, string targetChainId, CancellationToken ct = default)
    {
        return Task.FromResult(new OASISResult<bool>
        {
            IsError = false,
            Result = false,
            Message = $"{ChainType} VerifyBridgeProofAsync not implemented"
        });
    }

    public virtual bool SupportsBridging => false;

    // ─── Shared utility methods ───

    protected async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        int maxRetries = 3,
        int initialDelayMs = 1000,
        CancellationToken ct = default)
    {
        int retryCount = 0;
        int delayMs = initialDelayMs;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await operation();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (retryCount < maxRetries && IsRetryable(ex))
            {
                retryCount++;
                _logger.LogWarning(
                    ex,
                    "Retry {Retry}/{Max} for {ChainType} operation after {Delay}ms",
                    retryCount, maxRetries, ChainType, delayMs);
                await Task.Delay(delayMs, ct);
                delayMs = (int)(delayMs * 1.5);
            }
        }
    }

    protected async Task ExecuteWithRetryAsync(
        Func<Task> operation,
        int maxRetries = 3,
        int initialDelayMs = 1000,
        CancellationToken ct = default)
    {
        int retryCount = 0;
        int delayMs = initialDelayMs;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await operation();
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (retryCount < maxRetries && IsRetryable(ex))
            {
                retryCount++;
                _logger.LogWarning(
                    ex,
                    "Retry {Retry}/{Max} for {ChainType} operation after {Delay}ms",
                    retryCount, maxRetries, ChainType, delayMs);
                await Task.Delay(delayMs, ct);
                delayMs = (int)(delayMs * 1.5);
            }
        }
    }

    protected virtual bool IsRetryable(Exception ex)
    {
        return ex is HttpRequestException httpEx &&
               (httpEx.StatusCode == null || (int)httpEx.StatusCode >= 500 || (int)httpEx.StatusCode == 429);
    }

    protected OASISResult<T> Error<T>(string message, Exception? ex = null)
    {
        _logger.LogError(ex, "{ChainType} error: {Message}", ChainType, message);
        return new OASISResult<T>
        {
            IsError = true,
            Message = message,
            Exception = ex
        };
    }

    protected OASISResult<T> Ok<T>(T result, string message = "")
    {
        return new OASISResult<T>
        {
            IsError = false,
            Result = result,
            Message = message
        };
    }
}
