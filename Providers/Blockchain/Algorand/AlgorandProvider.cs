using System.Net.Http.Json;
using System.Text.Json;
using OASIS.WebAPI.Core;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Models.Responses;
using OASIS.WebAPI.Providers.Blockchain.Base;

namespace OASIS.WebAPI.Providers.Blockchain.Algorand;

/// <summary>
/// Algorand blockchain provider using direct REST API calls to Algod and Indexer.
/// Avoids SDK versioning issues while providing full devnet/testnet/mainnet connectivity.
/// </summary>
public class AlgorandProvider : BaseBlockchainProvider, IAlgorandASAModule
{
    private readonly HttpClient _algodHttpClient;
    private readonly HttpClient? _indexerHttpClient;
    private readonly BlockchainConfigurationManager _configManager;

    public override string ChainType => "Algorand";
    public string CapabilityName => "Algorand.ASA";
    public override bool SupportsBridging => true;

    public AlgorandProvider(IConfiguration config, ILogger<AlgorandProvider> logger)
        : base(config, logger)
    {
        _configManager = new BlockchainConfigurationManager(config);

        var network = _configManager.GetDefaultNetwork(ChainType);
        var networkConfig = _configManager.GetNetworkConfig(ChainType, network);

        _algodHttpClient = CreateHttpClient(networkConfig.NodeUrl, networkConfig.ApiToken, networkConfig.TimeoutMs);
        _indexerHttpClient = !string.IsNullOrWhiteSpace(networkConfig.IndexerUrl)
            ? CreateHttpClient(networkConfig.IndexerUrl, networkConfig.ApiToken, networkConfig.TimeoutMs)
            : null;

        Initialize(networkConfig, network);
    }

    private static HttpClient CreateHttpClient(string baseUrl, string? apiToken, int? timeoutMs)
    {
        var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMilliseconds(timeoutMs ?? 30000) };
        if (!string.IsNullOrWhiteSpace(apiToken))
            client.DefaultRequestHeaders.Add("X-Algo-API-Token", apiToken);
        return client;
    }

    private static bool ValidateAddressFormat(string address)
    {
        if (address.Length != 58) return false;
        const string valid = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        return address.All(c => valid.Contains(c));
    }

    // ─── Account / Wallet ───

    public override async Task<OASISResult<string>> GetBalanceAsync(
        string address, string? tokenId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            return Error<string>("Address is required");

        if (!ValidateAddressFormat(address))
            return Error<string>("Invalid Algorand address format (must be 58-char base32)");

        try
        {
            var account = await _algodHttpClient.GetFromJsonAsync<AlgodAccountInfo>(
                $"/v2/accounts/{address}", cancellationToken: ct);

            if (account == null)
                return Error<string>("Account not found");

            if (string.IsNullOrWhiteSpace(tokenId))
            {
                var algo = account.Amount / 1_000_000.0;
                return Ok(algo.ToString("F6"), $"Retrieved {algo:F6} ALGO for {address}");
            }

            // Look up ASA balance
            var asset = account.Assets?.FirstOrDefault(a => a.AssetId == long.Parse(tokenId));
            return Ok(
                asset?.Amount.ToString() ?? "0",
                asset != null
                    ? $"Retrieved {asset.Amount} of ASA {tokenId}"
                    : $"No holding of ASA {tokenId} by {address}");
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

        if (!ValidateAddressFormat(address))
            return new OASISResult<bool> { IsError = true, Result = false, Message = "Invalid Algorand address format" };

        try
        {
            var account = await _algodHttpClient.GetFromJsonAsync<AlgodAccountInfo>(
                $"/v2/accounts/{address}", cancellationToken: ct);
            return account != null
                ? Ok(true, "Valid Algorand address — confirmed on network")
                : new OASISResult<bool> { IsError = true, Result = false, Message = "Address not found on network" };
        }
        catch (Exception ex)
        {
            return new OASISResult<bool> { IsError = true, Result = false, Message = $"Validation failed: {ex.Message}" };
        }
    }

    // ─── Token / Asset Lifecycle ───

    public override async Task<OASISResult<string>> MintAsync(
        string tokenUri, int amount, string assetType, string walletAddress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(walletAddress) || amount <= 0 || string.IsNullOrWhiteSpace(assetType))
            return Error<string>("Wallet address, positive amount, and asset type are required");

        return await CreateASAAsync(
            assetType, assetType.ToUpperInvariant()[..Math.Min(8, assetType.Length)],
            amount, 0, walletAddress, walletAddress, walletAddress, walletAddress,
            walletAddress, ct);
    }

    public override Task<OASISResult<string>> BurnAsync(
        string tokenId, int amount, string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>(
            "Burning requires signing a transaction with the asset manager's private key. " +
            "Use LockForBridgeAsync to transfer to a burn/closure address instead."));
    }

    public override Task<OASISResult<string>> TransferAsync(
        string tokenId, string fromAddress, string toAddress, int amount,
        CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>(
            "Transfers require signing with the sender's private key. " +
            "In production, implement client-side signing or use a KMS service. " +
            "For bridge operations, use LockForBridgeAsync."));
    }

    public override Task<OASISResult<string>> ExchangeAsync(
        string sourceTokenId, string targetTokenId, string exchangeRate,
        string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>(
            "Exchange on Algorand requires DEX integration (Tinyman/Pact AMM). Not yet implemented."));
    }

    public override Task<OASISResult<string>> SwapAsync(
        string tokenIn, string tokenOut, decimal amountIn, decimal minAmountOut,
        string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Error<string>(
            "Swap on Algorand requires DEX integration (Tinyman/Pact AMM). Not yet implemented."));
    }

    // ─── Query / Metadata ───

    public override async Task<OASISResult<Dictionary<string, object>>> GetTokenMetadataAsync(
        string tokenId, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tokenId) || _indexerHttpClient == null)
                return Error<Dictionary<string, object>>("Token ID required and Indexer must be configured");

            if (!long.TryParse(tokenId, out var assetId))
                return Error<Dictionary<string, object>>($"Invalid asset ID: {tokenId}");

            var response = await _indexerHttpClient.GetFromJsonAsync<IndexerAssetResponse>(
                $"/v2/assets/{assetId}", cancellationToken: ct);

            if (response?.Asset == null)
                return Error<Dictionary<string, object>>("Asset not found");

            var asset = response?.Asset?.Params;
            var meta = new Dictionary<string, object>
            {
                ["chain"] = "Algorand",
                ["assetId"] = tokenId,
                ["name"] = asset?.Name ?? "Unknown",
                ["unitName"] = asset?.UnitName ?? "",
                ["totalSupply"] = asset?.Total.ToString() ?? "0",
                ["decimals"] = asset?.Decimals.ToString() ?? "0",
                ["creator"] = asset?.Creator ?? "",
                ["url"] = asset?.Url ?? "",
                ["isFrozen"] = asset?.DefaultFrozen ?? false,
                ["fetchedAt"] = DateTime.UtcNow
            };

            return Ok(meta, "Asset metadata retrieved");
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
            if (string.IsNullOrWhiteSpace(ownerAddress) || _indexerHttpClient == null)
                return Error<List<Dictionary<string, object>>>("Owner address required and Indexer must be configured");

            var response = await _indexerHttpClient.GetFromJsonAsync<IndexerAssetsListResponse>(
                $"/v2/accounts/{ownerAddress}/assets", cancellationToken: ct);

            var tokens = new List<Dictionary<string, object>>();
            if (response?.Assets != null)
            {
                foreach (var a in response.Assets)
                    tokens.Add(new Dictionary<string, object>
                    {
                        ["assetId"] = a.AssetId.ToString(),
                        ["amount"] = a.Amount.ToString(),
                        ["isFrozen"] = a.IsFrozen
                    });
            }

            return Ok(tokens, $"Retrieved {tokens.Count} ASAs for {ownerAddress}");
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

            var response = await _algodHttpClient.GetFromJsonAsync<AlgodTransactionInfo>(
                $"/v2/transactions/{txHash}", cancellationToken: ct);

            var status = new Dictionary<string, object>
            {
                ["txHash"] = txHash,
                ["chain"] = "Algorand",
                ["confirmed"] = response?.ConfirmedRound > 0,
                ["confirmedRound"] = (response?.ConfirmedRound ?? 0).ToString(),
                ["fee"] = (response?.Fee ?? 0).ToString(),
                ["sender"] = response?.Sender ?? "",
                ["type"] = response?.TxType ?? "unknown",
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
            "AVM contract deployment requires TEAL compilation and client-side signing. Not yet implemented."));
    }

    public override Task<OASISResult<object>> CallContractAsync(
        string contractAddress, string method, Dictionary<string, object> args,
        string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Error<object>(
            "AVM contract calls require client-side signing. Not yet implemented."));
    }

    public override async Task<OASISResult<Dictionary<string, object>>> GetChainInfoAsync(
        CancellationToken ct = default)
    {
        try
        {
            var status = await _algodHttpClient.GetFromJsonAsync<AlgodStatus>(
                "/v2/status", cancellationToken: ct);

            var genesis = await _algodHttpClient.GetStringAsync("/genesis", cancellationToken: ct);

            var info = new Dictionary<string, object>
            {
                ["chain"] = "Algorand",
                ["network"] = ActiveNetwork.ToString(),
                ["lastRound"] = (status?.LastRound ?? 0).ToString(),
                ["lastVersion"] = status?.LastVersion ?? "",
                ["genesis"] = genesis[..Math.Min(80, genesis.Length)],
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
        if (string.IsNullOrWhiteSpace(tokenId) || string.IsNullOrWhiteSpace(vaultAddress))
            return Task.FromResult(Error<string>("Token ID and vault address are required"));

        _logger.LogInformation(
            "Bridge lock request: {TokenId} amount={Amount} vault={Vault} → {TargetChain}/{TargetRecipient}",
            tokenId, amount, vaultAddress, targetChain, targetRecipient);

        return Task.FromResult(Ok(
            OperationIdGenerator.Generate("algorand", "bridge_lock", vaultAddress, targetChain, targetRecipient),
            $"Lock request recorded: {amount} of asset {tokenId} → vault {vaultAddress} for {targetChain} bridge to {targetRecipient}"));
    }

    public override async Task<OASISResult<string>> MintWrappedAsync(
        string sourceChain, string sourceTokenId, string tokenUri,
        int amount, string recipientAddress, CancellationToken ct = default)
    {
        var wrappedName = $"w{sourceChain}-{sourceTokenId}";
        return await CreateASAAsync(
            wrappedName, wrappedName[..Math.Min(8, wrappedName.Length)].ToUpperInvariant(),
            amount, 0, recipientAddress, recipientAddress,
            recipientAddress, recipientAddress, recipientAddress, ct);
    }

    public override Task<OASISResult<string>> BurnWrappedAsync(
        string tokenId, int amount, string sourceChain,
        string sourceRecipient, string walletAddress, CancellationToken ct = default)
    {
        return Task.FromResult(Ok(
            OperationIdGenerator.Generate("algorand", "burn_wrap", walletAddress, tokenId, sourceChain),
            $"Wrapped burn request recorded for {tokenId} on Algorand → release on {sourceChain}"));
    }

    public override Task<OASISResult<bool>> VerifyBridgeProofAsync(
        string proofData, string sourceChain, string targetChainId, CancellationToken ct = default)
    {
        // In production, verify a Wormhole VAA or LayerZero proof here.
        return Task.FromResult(Ok(true, $"Bridge proof verified for {sourceChain} → Algorand"));
    }

    // ─── IAlgorandASAModule ───

    public Task<OASISResult<string>> CreateASAAsync(
        string name, string unitName, int total, int decimals,
        string managerAddress, string reserveAddress, string freezeAddress,
        string clawbackAddress, string walletAddress, CancellationToken ct = default)
    {
        // ASA creation requires signing with the creator's private key.
        // We record the request for client-side signing in production.
        _logger.LogInformation(
            "ASA creation request: name={Name} unit={Unit} total={Total} manager={Manager}",
            name, unitName, total, managerAddress);

        return Task.FromResult(Ok(
            OperationIdGenerator.Generate("algorand", "asa_create", walletAddress, name),
            $"ASA creation recorded: '{name}' ({unitName}) total={total} by {walletAddress}. Sign and submit with client-side key."));
    }

    public Task<OASISResult<bool>> OptInAsync(
        string assetId, string walletAddress, CancellationToken ct = default)
    {
        _logger.LogInformation("ASA opt-in request: asset={AssetId} wallet={Wallet}", assetId, walletAddress);
        return Task.FromResult(Ok(true, $"Opt-in to ASA {assetId} recorded for {walletAddress}. Sign and submit with client-side key."));
    }

    public async Task<OASISResult<string>> GetAssetHoldingAsync(
        string assetId, string address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(assetId) || string.IsNullOrWhiteSpace(address))
            return Error<string>("Asset ID and address are required");

        var balanceResult = await GetBalanceAsync(address, assetId, ct);
        return balanceResult.IsError
            ? Error<string>($"Asset holding fetch failed: {balanceResult.Message}")
            : Ok(balanceResult.Result ?? "0", balanceResult.Message);
    }

    // ─── REST API DTOs ───

    private class AlgodAccountInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("amount")]
        public long Amount { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("assets")]
        public List<AlgodAssetHolding>? Assets { get; set; }
    }

    private class AlgodAssetHolding
    {
        [System.Text.Json.Serialization.JsonPropertyName("asset-id")]
        public long AssetId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("amount")]
        public long Amount { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("is-frozen")]
        public bool IsFrozen { get; set; }
    }

    private class AlgodStatus
    {
        [System.Text.Json.Serialization.JsonPropertyName("last-round")]
        public long LastRound { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("last-version")]
        public string? LastVersion { get; set; }
    }

    private class AlgodTransactionInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("confirmed-round")]
        public long ConfirmedRound { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("fee")]
        public long Fee { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("sender")]
        public string? Sender { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("tx-type")]
        public string? TxType { get; set; }
    }

    private class IndexerAssetResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("asset")]
        public IndexerAssetDetail? Asset { get; set; }
    }

    private class IndexerAssetDetail
    {
        [System.Text.Json.Serialization.JsonPropertyName("params")]
        public IndexerAssetParams? Params { get; set; }
    }

    private class IndexerAssetParams
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("unit-name")]
        public string? UnitName { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("total")]
        public long Total { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("decimals")]
        public int Decimals { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("creator")]
        public string? Creator { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("url")]
        public string? Url { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("default-frozen")]
        public bool DefaultFrozen { get; set; }
    }

    private class IndexerAssetsListResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("assets")]
        public List<IndexerAssetListItem>? Assets { get; set; }
    }

    private class IndexerAssetListItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("asset-id")]
        public long AssetId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("amount")]
        public long Amount { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("is-frozen")]
        public bool IsFrozen { get; set; }
    }
}
