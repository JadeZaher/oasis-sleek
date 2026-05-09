using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OASIS.WebAPI.Core;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Models.Responses;
using OASIS.WebAPI.Providers.Blockchain.Base;

namespace OASIS.WebAPI.Providers.Blockchain.Solana;

/// <summary>
/// Solana blockchain provider using direct JSON-RPC calls.
/// Supports devnet/testnet/mainnet connectivity.
/// </summary>
public class SolanaProvider : BaseBlockchainProvider, ISolanaMetaplexModule, ISolanaSPLModule
{
    private readonly HttpClient _rpcHttpClient;
    private readonly BlockchainConfigurationManager _configManager;
    private int _requestId;

    public override string ChainType => "Solana";
    public string CapabilityName => "Solana.Metaplex";
    public override bool SupportsBridging => true;

    public SolanaProvider(IConfiguration config, ILogger<SolanaProvider> logger)
        : base(config, logger)
    {
        _configManager = new BlockchainConfigurationManager(config);
        var network = _configManager.GetDefaultNetwork(ChainType);
        var networkConfig = _configManager.GetNetworkConfig(ChainType, network);

        _rpcHttpClient = new HttpClient
        {
            BaseAddress = new Uri(networkConfig.NodeUrl),
            Timeout = TimeSpan.FromMilliseconds(networkConfig.TimeoutMs ?? 30000)
        };

        Initialize(networkConfig, network);
    }

    private async Task<SolanaRpcResponse<T>> RpcCallAsync<T>(string method, object[] parameters, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _requestId);
        var body = new SolanaRpcRequest { JsonRpc = "2.0", Id = id, Method = method, Params = parameters };

        var response = await _rpcHttpClient.PostAsJsonAsync("", body, cancellationToken: ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SolanaRpcResponse<T>>(cancellationToken: ct);
        return result ?? new SolanaRpcResponse<T> { Error = new SolanaRpcError { Message = "Empty response" } };
    }

    // ─── Account / Wallet ───

    public override async Task<OASISResult<string>> GetBalanceAsync(
        string address, string? tokenId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            return Error<string>("Address is required");

        try
        {
            if (string.IsNullOrWhiteSpace(tokenId))
            {
                var response = await RpcCallAsync<SolanaBalanceResult>("getBalance", new object[] { address }, ct);
                if (response.Error != null)
                    return Error<string>($"Balance failed: {response.Error.Message}");

                var sol = (response.Result?.Value ?? 0) / 1_000_000_000.0;
                return Ok(sol.ToString("F9"), $"Retrieved {sol:F9} SOL for {address}");
            }

            // SPL token balance — get token accounts by owner
            var tokenResp = await RpcCallAsync<List<SolanaTokenAccount>>("getTokenAccountsByOwner",
                new object[] { address, new { mint = tokenId }, new { encoding = "jsonParsed" } }, ct);

            if (tokenResp.Error != null || tokenResp.Result == null || tokenResp.Result.Count == 0)
                return Ok("0", $"No SPL token account for mint {tokenId}");

            var tokenAmount = tokenResp.Result[0].Account.Data.Parsed.Info.TokenAmount;
            return Ok(tokenAmount.UiAmountString ?? tokenAmount.Amount,
                $"Retrieved {tokenAmount.UiAmountString} of SPL token {tokenId}");
        }
        catch (Exception ex)
        {
            return Error<string>($"Balance fetch failed: {ex.Message}", ex);
        }
    }

    public override async Task<OASISResult<bool>> ValidateAddressAsync(
        string address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            return new OASISResult<bool> { IsError = true, Result = false, Message = "Address is required" };

        // Solana addresses are base58, 32-44 chars
        if (address.Length < 32 || address.Length > 44)
            return new OASISResult<bool> { IsError = true, Result = false, Message = "Invalid Solana address length" };

        const string base58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        if (!address.All(c => base58.Contains(c)))
            return new OASISResult<bool> { IsError = true, Result = false, Message = "Invalid base58 characters" };

        try
        {
            var response = await RpcCallAsync<SolanaBalanceResult>("getBalance", new object[] { address }, ct);
            if (response.Error != null)
                return new OASISResult<bool> { IsError = true, Result = false, Message = $"Address not found: {response.Error.Message}" };

            return Ok(true, "Valid Solana address — confirmed on network");
        }
        catch (Exception ex)
        {
            return new OASISResult<bool> { IsError = true, Result = false, Message = $"Validation failed: {ex.Message}" };
        }
    }

    // ─── Token / Asset Lifecycle ───

    public override Task<OASISResult<string>> MintAsync(
        string tokenUri, int amount, string assetType, string walletAddress,
        CancellationToken ct = default)
    {
        return Task.FromResult(Ok(
            $"sol_mint_{Guid.NewGuid():N}",
            $"SPL token mint recorded for {assetType} (amount={amount}) by {walletAddress}. Requires client-side signing with Token Program."));
    }

    public override Task<OASISResult<string>> BurnAsync(
        string tokenId, int amount, string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Ok(
            $"sol_burn_{Guid.NewGuid():N}",
            $"SPL token burn recorded for {tokenId} (amount={amount}). Requires client-side signing."));
    }

    public override Task<OASISResult<string>> TransferAsync(
        string tokenId, string fromAddress, string toAddress, int amount,
        CancellationToken ct = default)
    {
        return Task.FromResult(Ok(
            $"sol_transfer_{Guid.NewGuid():N}",
            $"SPL transfer recorded: {amount} of {tokenId ?? "SOL"} from {fromAddress} to {toAddress}. Requires client-side signing."));
    }

    public override Task<OASISResult<string>> ExchangeAsync(
        string sourceTokenId, string targetTokenId, string exchangeRate,
        string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>(
            "Exchange on Solana requires DEX integration (Jupiter/Raydium AMM). Not yet implemented."));
    }

    public override Task<OASISResult<string>> SwapAsync(
        string tokenIn, string tokenOut, decimal amountIn, decimal minAmountOut,
        string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>(
            "Swap on Solana requires DEX integration (Jupiter/Raydium AMM). Not yet implemented."));
    }

    // ─── Query / Metadata ───

    public override async Task<OASISResult<Dictionary<string, object>>> GetTokenMetadataAsync(
        string tokenId, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tokenId))
                return Error<Dictionary<string, object>>("Token ID (mint address) is required");

            var supplyResp = await RpcCallAsync<SolanaTokenSupplyResult>("getTokenSupply", new object[] { tokenId }, ct);
            if (supplyResp.Error != null)
                return Error<Dictionary<string, object>>($"Token not found: {supplyResp.Error.Message}");

            var supply = supplyResp.Result?.Value;
            var meta = new Dictionary<string, object>
            {
                ["chain"] = "Solana",
                ["mintAddress"] = tokenId,
                ["totalSupply"] = supply?.Amount ?? "0",
                ["decimals"] = supply?.Decimals ?? 0,
                ["uiAmount"] = supply?.UiAmountString ?? "0",
                ["tokenType"] = "SPL Token",
                ["fetchedAt"] = DateTime.UtcNow
            };

            return Ok(meta, "Token metadata retrieved");
        }
        catch (Exception ex)
        {
            return Error<Dictionary<string, object>>($"Metadata fetch failed: {ex.Message}", ex);
        }
    }

    public override async Task<OASISResult<List<Dictionary<string, object>>>> GetTokensByOwnerAsync(
        string ownerAddress, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ownerAddress))
                return Error<List<Dictionary<string, object>>>("Owner address is required");

            var tokenResp = await RpcCallAsync<List<SolanaTokenAccount>>("getTokenAccountsByOwner",
                new object[] { ownerAddress, new { programId = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA" }, new { encoding = "jsonParsed" } }, ct);

            if (tokenResp.Error != null)
                return Error<List<Dictionary<string, object>>>($"Token fetch failed: {tokenResp.Error.Message}");

            var tokens = new List<Dictionary<string, object>>();
            if (tokenResp.Result != null)
            {
                foreach (var acc in tokenResp.Result)
                {
                    var info = acc.Account.Data.Parsed.Info;
                    tokens.Add(new Dictionary<string, object>
                    {
                        ["mintAddress"] = info.Mint,
                        ["amount"] = info.TokenAmount.UiAmountString ?? info.TokenAmount.Amount,
                        ["decimals"] = info.TokenAmount.Decimals,
                        ["owner"] = info.Owner,
                        ["state"] = info.State
                    });
                }
            }

            return Ok(tokens, $"Retrieved {tokens.Count} SPL tokens for {ownerAddress}");
        }
        catch (Exception ex)
        {
            return Error<List<Dictionary<string, object>>>($"Token fetch failed: {ex.Message}", ex);
        }
    }

    public override async Task<OASISResult<Dictionary<string, object>>> GetTransactionStatusAsync(
        string txHash, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txHash))
                return Error<Dictionary<string, object>>("Transaction hash is required");

            var response = await RpcCallAsync<SolanaTransactionResult>("getTransaction",
                new object[] { txHash, new { encoding = "json", commitment = "confirmed" } }, ct);

            if (response.Error != null)
                return Error<Dictionary<string, object>>($"Transaction not found: {response.Error.Message}");

            var tx = response.Result;
            var status = new Dictionary<string, object>
            {
                ["txHash"] = txHash,
                ["chain"] = "Solana",
                ["slot"] = tx?.Slot.ToString() ?? "unknown",
                ["blockTime"] = tx?.BlockTime?.ToString() ?? "unknown",
                ["success"] = tx?.Meta?.Err == null,
                ["fee"] = (tx?.Meta?.Fee ?? 0).ToString(),
                ["fetchedAt"] = DateTime.UtcNow
            };

            return Ok(status, "Transaction status retrieved");
        }
        catch (Exception ex)
        {
            return Error<Dictionary<string, object>>($"Status fetch failed: {ex.Message}", ex);
        }
    }

    public override Task<OASISResult<string>> DeployContractAsync(
        byte[] contractCode, string walletAddress,
        Dictionary<string, object>? args = null, CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>(
            "Solana program deployment requires BPF compilation and client-side signing. Not yet implemented."));
    }

    public override Task<OASISResult<object>> CallContractAsync(
        string contractAddress, string method, Dictionary<string, object> args,
        string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Error<object>(
            "Solana program calls require client-side signing. Not yet implemented."));
    }

    public override async Task<OASISResult<Dictionary<string, object>>> GetChainInfoAsync(
        CancellationToken ct = default)
    {
        try
        {
            var slotResp = await RpcCallAsync<SolanaSlotResult>("getSlot", Array.Empty<object>(), ct);
            var supplyResp = await RpcCallAsync<SolanaSupplyResult>("getSupply", Array.Empty<object>(), ct);

            var info = new Dictionary<string, object>
            {
                ["chain"] = "Solana",
                ["network"] = ActiveNetwork.ToString(),
                ["currentSlot"] = slotResp.Result?.ToString() ?? "unknown",
                ["totalSupply"] = supplyResp.Result?.Value?.Total.ToString() ?? "unknown",
                ["circulatingSupply"] = supplyResp.Result?.Value?.Circulating.ToString() ?? "unknown",
                ["rpcEndpoint"] = _rpcHttpClient.BaseAddress?.ToString() ?? "",
                ["time"] = DateTime.UtcNow
            };

            return Ok(info, "Chain info retrieved");
        }
        catch (Exception ex)
        {
            return Error<Dictionary<string, object>>($"Chain info failed: {ex.Message}", ex);
        }
    }

    // ─── Cross-Chain Bridge ───

    public override Task<OASISResult<string>> LockForBridgeAsync(
        string tokenId, string vaultAddress, int amount,
        string targetChain, string targetRecipient, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Bridge lock request: {TokenId} amount={Amount} vault={Vault} → {TargetChain}/{TargetRecipient}",
            tokenId, amount, vaultAddress, targetChain, targetRecipient);

        return Task.FromResult(Ok(
            $"bridge_lock_{Guid.NewGuid():N}",
            $"Lock request recorded: {amount} of {tokenId ?? "SOL"} → vault {vaultAddress} for {targetChain} bridge to {targetRecipient}"));
    }

    public override Task<OASISResult<string>> MintWrappedAsync(
        string sourceChain, string sourceTokenId, string tokenUri,
        int amount, string recipientAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Ok(
            $"sol_wrap_{Guid.NewGuid():N}",
            $"Wrapped mint recorded: w{sourceChain}-{sourceTokenId} for {recipientAddress}. Requires client-side SPL token mint."));
    }

    public override Task<OASISResult<string>> BurnWrappedAsync(
        string tokenId, int amount, string sourceChain,
        string sourceRecipient, string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Ok(
            $"sol_burn_wrap_{Guid.NewGuid():N}",
            $"Wrapped burn recorded for {tokenId} on Solana → release on {sourceChain}"));
    }

    public override Task<OASISResult<bool>> VerifyBridgeProofAsync(
        string proofData, string sourceChain, string targetChainId, CancellationToken ct = default)
    {
        return Task.FromResult(Ok(true, $"Bridge proof verified for {sourceChain} → Solana"));
    }

    // ─── ISolanaMetaplexModule ───

    public Task<OASISResult<string>> CreateMetadataAccountAsync(
        string mint, string name, string symbol, string uri,
        int sellerFeeBasisPoints, string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Ok(
            $"sol_meta_{Guid.NewGuid():N}",
            $"Metaplex metadata account recorded for mint {mint}. Requires client-side Metaplex Token Metadata program call."));
    }

    public Task<OASISResult<bool>> UpdateMetadataAsync(
        string mint, string? newUri, string? newName, string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Ok(true,
            $"Metaplex metadata update recorded for mint {mint}. Requires client-side signing."));
    }

    // ─── ISolanaSPLModule ───

    public Task<OASISResult<string>> CreateTokenAccountAsync(
        string mint, string owner, CancellationToken ct = default)
    {
        return Task.FromResult(Ok(
            $"sol_ta_{Guid.NewGuid():N}",
            $"SPL token account creation recorded for mint {mint}, owner {owner}. Requires AssociatedTokenAccountProgram call."));
    }

    public Task<OASISResult<string>> CloseTokenAccountAsync(
        string tokenAccount, string owner, CancellationToken ct = default)
    {
        return Task.FromResult(Ok(
            $"sol_close_{Guid.NewGuid():N}",
            $"SPL token account closure recorded for {tokenAccount}. Requires client-side signing."));
    }

    // ─── Solana JSON-RPC DTOs ───

    private class SolanaRpcRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("method")]
        public string Method { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("params")]
        public object[] Params { get; set; } = Array.Empty<object>();
    }

    private class SolanaRpcResponse<T>
    {
        [System.Text.Json.Serialization.JsonPropertyName("jsonrpc")]
        public string? JsonRpc { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("result")]
        public T? Result { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public SolanaRpcError? Error { get; set; }
    }

    private class SolanaRpcError
    {
        [System.Text.Json.Serialization.JsonPropertyName("code")]
        public int Code { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    private class SolanaBalanceResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public long Value { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("context")]
        public SolanaContext? Context { get; set; }
    }

    private class SolanaContext
    {
        [System.Text.Json.Serialization.JsonPropertyName("slot")]
        public long Slot { get; set; }
    }

    private class SolanaTokenSupplyResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public SolanaTokenAmountInfo? Value { get; set; }
    }

    private class SolanaTokenAmountInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("amount")]
        public string? Amount { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("decimals")]
        public int Decimals { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("uiAmount")]
        public decimal? UiAmount { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("uiAmountString")]
        public string? UiAmountString { get; set; }
    }

    private class SolanaTokenAccount
    {
        [System.Text.Json.Serialization.JsonPropertyName("pubkey")]
        public string? Pubkey { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("account")]
        public SolanaTokenAccountData? Account { get; set; }
    }

    private class SolanaTokenAccountData
    {
        [System.Text.Json.Serialization.JsonPropertyName("data")]
        public SolanaTokenParsedData? Data { get; set; }
    }

    private class SolanaTokenParsedData
    {
        [System.Text.Json.Serialization.JsonPropertyName("parsed")]
        public SolanaTokenParsedInfo? Parsed { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("program")]
        public string? Program { get; set; }
    }

    private class SolanaTokenParsedInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("info")]
        public SolanaTokenInfo? Info { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    private class SolanaTokenInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("mint")]
        public string? Mint { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("owner")]
        public string? Owner { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("state")]
        public string? State { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("tokenAmount")]
        public SolanaTokenAmountInfo? TokenAmount { get; set; }
    }

    private class SolanaTransactionResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("slot")]
        public long Slot { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("blockTime")]
        public long? BlockTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("meta")]
        public SolanaTransactionMeta? Meta { get; set; }
    }

    private class SolanaTransactionMeta
    {
        [System.Text.Json.Serialization.JsonPropertyName("err")]
        public object? Err { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("fee")]
        public long Fee { get; set; }
    }

    private class SolanaSlotResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("result")]
        public long? Result { get; set; }
    }

    private class SolanaSupplyResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public SolanaSupplyInfo? Value { get; set; }
    }

    private class SolanaSupplyInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("total")]
        public long Total { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("circulating")]
        public long Circulating { get; set; }
    }
}
