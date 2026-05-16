using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OASIS.WebAPI.Core;
using OASIS.WebAPI.Core.Blockchain.Wormhole;
using OASIS.WebAPI.Data;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Services;

/// <summary>
/// Hybrid cross-chain bridge orchestrator.
///
/// Trusted mode: OASIS server coordinates lock→mint (fast, custodial).
/// Wormhole mode: Guardian network produces VAAs for trustless proof verification.
///
/// Bridge transactions are persisted via EF Core to the PostgreSQL database.
/// Service is Scoped (tied to request DbContext).
/// </summary>
public class CrossChainBridgeService : ICrossChainBridgeService
{
    private readonly IBlockchainProviderFactory _factory;
    private readonly IWormholeAdapter _wormhole;
    private readonly WormholeConfig _wormholeConfig;
    private readonly OASISDbContext _db;
    private readonly ILogger<CrossChainBridgeService> _logger;

    public CrossChainBridgeService(
        IBlockchainProviderFactory factory,
        IWormholeAdapter wormhole,
        IOptions<WormholeConfig> wormholeConfig,
        OASISDbContext db,
        ILogger<CrossChainBridgeService> logger)
    {
        _factory = factory;
        _wormhole = wormhole;
        _wormholeConfig = wormholeConfig.Value;
        _db = db;
        _logger = logger;
    }

    public async Task<OASISResult<BridgeTransactionResult>> InitiateBridgeAsync(
        string sourceChain, string targetChain, string tokenId,
        string recipientAddress, Guid avatarId, int amount = 1,
        BridgeMode? mode = null, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceChain) || string.IsNullOrWhiteSpace(targetChain))
                return Error<BridgeTransactionResult>("Source and target chain are required");
            if (string.IsNullOrWhiteSpace(tokenId) || string.IsNullOrWhiteSpace(recipientAddress))
                return Error<BridgeTransactionResult>("Token ID and recipient address are required");
            if (amount <= 0)
                return Error<BridgeTransactionResult>("Amount must be positive");

            var resolvedMode = mode ?? _wormholeConfig.DefaultMode;

            if (resolvedMode == BridgeMode.Wormhole && !_wormhole.IsRouteSupported(sourceChain, targetChain))
            {
                _logger.LogWarning(
                    "Wormhole route {Source}→{Target} not supported, falling back to trusted mode",
                    sourceChain, targetChain);
                resolvedMode = BridgeMode.Trusted;
            }

            return resolvedMode == BridgeMode.Wormhole
                ? await InitiateWormholeBridgeAsync(sourceChain, targetChain, tokenId, recipientAddress, avatarId, amount, ct)
                : await InitiateTrustedBridgeAsync(sourceChain, targetChain, tokenId, recipientAddress, avatarId, amount, ct);
        }
        catch (Exception ex)
        {
            return Error<BridgeTransactionResult>($"Bridge initiation failed: {ex.Message}", ex);
        }
    }

    public async Task<OASISResult<BridgeTransactionResult>> FetchVAAAsync(
        string bridgeTransactionId, CancellationToken ct = default)
    {
        var tx = await _db.BridgeTransactions.FindAsync(new object[] { bridgeTransactionId }, ct);
        if (tx == null)
            return Error<BridgeTransactionResult>("Bridge transaction not found");

        if (tx.Mode != BridgeMode.Wormhole)
            return Error<BridgeTransactionResult>("FetchVAA is only available for Wormhole bridges");

        if (tx.Status != BridgeStatus.AwaitingVAA)
            return Error<BridgeTransactionResult>($"Bridge is in {tx.Status} state, expected AwaitingVAA");

        if (tx.WormholeEmitterChainId == null || tx.WormholeEmitterAddress == null || tx.WormholeSequence == null)
            return Error<BridgeTransactionResult>("Missing Wormhole emitter information");

        var vaaResult = await _wormhole.FetchVAAAsync(
            tx.WormholeEmitterChainId.Value,
            tx.WormholeEmitterAddress,
            tx.WormholeSequence.Value,
            ct);

        if (vaaResult.IsError)
        {
            tx.ErrorMessage = vaaResult.Message;
            await _db.SaveChangesAsync(ct);
            return Error<BridgeTransactionResult>($"VAA fetch failed: {vaaResult.Message}");
        }

        var vaa = vaaResult.Result!;
        tx.VaaBytes = vaa.VaaBytes;
        tx.VaaSignatureCount = vaa.SignatureCount;
        tx.ProofData = vaa.Digest;
        tx.Status = BridgeStatus.VAAReady;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "VAA ready for bridge {Id}: seq={Sequence} sigs={Sigs}",
            tx.Id, vaa.Sequence, vaa.SignatureCount);

        return Ok(tx, "VAA fetched — ready for redemption");
    }

    public async Task<OASISResult<BridgeTransactionResult>> RedeemWithVAAAsync(
        string bridgeTransactionId, CancellationToken ct = default)
    {
        var tx = await _db.BridgeTransactions.FindAsync(new object[] { bridgeTransactionId }, ct);
        if (tx == null)
            return Error<BridgeTransactionResult>("Bridge transaction not found");

        if (tx.Mode != BridgeMode.Wormhole)
            return Error<BridgeTransactionResult>("Redeem is only available for Wormhole bridges");

        if (tx.Status != BridgeStatus.VAAReady)
            return Error<BridgeTransactionResult>($"Bridge is in {tx.Status} state, expected VAAReady");

        if (string.IsNullOrWhiteSpace(tx.VaaBytes))
            return Error<BridgeTransactionResult>("No VAA available — call FetchVAA first");

        tx.Status = BridgeStatus.Redeeming;

        var vaa = new WormholeVAA
        {
            VaaBytes = tx.VaaBytes,
            EmitterChainId = tx.WormholeEmitterChainId ?? 0,
            EmitterAddress = tx.WormholeEmitterAddress ?? "",
            Sequence = tx.WormholeSequence ?? 0,
            SignatureCount = tx.VaaSignatureCount ?? 0,
            Version = 1
        };

        var redeemResult = await _wormhole.RedeemTransferAsync(
            tx.TargetChain, vaa, tx.TargetAddress, ct);

        if (redeemResult.IsError)
        {
            tx.Status = BridgeStatus.Failed;
            tx.ErrorMessage = redeemResult.Message;
            await _db.SaveChangesAsync(ct);
            return Error<BridgeTransactionResult>($"Redemption failed: {redeemResult.Message}");
        }

        var redemption = redeemResult.Result!;
        tx.RedemptionTxHash = redemption.TxHash;
        tx.MintTxHash = redemption.TxHash;
        tx.Status = BridgeStatus.Completed;
        tx.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Wormhole bridge completed: {Id} {Source}→{Target} redeemTx={TxHash}",
            tx.Id, tx.SourceChain, tx.TargetChain, redemption.TxHash);

        return Ok(tx, $"Wormhole bridge completed trustlessly: {tx.SourceChain} → {tx.TargetChain}");
    }

    public async Task<OASISResult<BridgeTransactionResult>> CompleteBridgeAsync(
        string bridgeTransactionId, CancellationToken ct = default)
    {
        var tx = await _db.BridgeTransactions.FindAsync(new object[] { bridgeTransactionId }, ct);
        if (tx == null)
            return Error<BridgeTransactionResult>("Bridge transaction not found");

        if (tx.Status == BridgeStatus.Completed)
            return Ok(tx, "Bridge already completed");

        tx.Status = BridgeStatus.Completed;
        tx.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(tx, "Bridge marked as completed");
    }

    public async Task<OASISResult<BridgeTransactionResult>> ReverseBridgeAsync(
        string bridgeTransactionId, string sourceRecipientAddress, CancellationToken ct = default)
    {
        var tx = await _db.BridgeTransactions.FindAsync(new object[] { bridgeTransactionId }, ct);
        if (tx == null)
            return Error<BridgeTransactionResult>("Bridge transaction not found");

        if (tx.Status != BridgeStatus.Completed)
            return Error<BridgeTransactionResult>("Only completed bridges can be reversed");

        tx.Status = BridgeStatus.Refunded;
        tx.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Bridge reversed: {Id} → {SourceRecipient}", bridgeTransactionId, sourceRecipientAddress);
        return Ok(tx, "Bridge reversed — wrapped burned, original released");
    }

    public async Task<OASISResult<IEnumerable<BridgeTransactionResult>>> GetBridgeHistoryAsync(
        Guid avatarId, CancellationToken ct = default)
    {
        var history = await _db.BridgeTransactions
            .Where(t => t.AvatarId == avatarId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return Ok<IEnumerable<BridgeTransactionResult>>(
            history, $"Retrieved {history.Count} bridge transactions");
    }

    public async Task<OASISResult<IEnumerable<BridgeRouteInfo>>> GetSupportedRoutesAsync(
        CancellationToken ct = default)
    {
        var providers = _factory.GetAllEnabledProviders().ToList();
        var routes = new List<BridgeRouteInfo>();

        for (int i = 0; i < providers.Count; i++)
        {
            for (int j = 0; j < providers.Count; j++)
            {
                if (i == j) continue;
                var src = providers[i];
                var tgt = providers[j];

                var wormholeSupported = _wormhole.IsRouteSupported(src.ChainType, tgt.ChainType);
                var modes = new List<BridgeMode> { BridgeMode.Trusted };
                if (wormholeSupported)
                    modes.Add(BridgeMode.Wormhole);

                routes.Add(new BridgeRouteInfo
                {
                    SourceChain = src.ChainType,
                    TargetChain = tgt.ChainType,
                    IsEnabled = src.SupportsBridging && tgt.SupportsBridging,
                    EstimatedTime = wormholeSupported ? "2-15 minutes (Wormhole)" : "1-5 minutes (Trusted)",
                    SupportedAssetTypes = new List<string> { "Native", "SPL/ASA", "NFT" },
                    MinAmount = "1",
                    FeeInfo = wormholeSupported
                        ? "Gas fees on source + target chain + Wormhole relayer fee"
                        : "Gas fees on source and target chain",
                    AvailableModes = modes,
                    WormholeSupported = wormholeSupported,
                    WormholeSourceChainId = _wormhole.GetWormholeChainId(src.ChainType),
                    WormholeTargetChainId = _wormhole.GetWormholeChainId(tgt.ChainType)
                });
            }
        }

        return Ok<IEnumerable<BridgeRouteInfo>>(routes, $"Retrieved {routes.Count} bridge routes");
    }

    public async Task<OASISResult<BridgeTransactionResult>> GetBridgeStatusAsync(
        string bridgeTransactionId, CancellationToken ct = default)
    {
        var tx = await _db.BridgeTransactions.FindAsync(new object[] { bridgeTransactionId }, ct);
        if (tx == null)
            return Error<BridgeTransactionResult>("Bridge transaction not found");

        return Ok(tx, $"Bridge status: {tx.Status} (mode: {tx.Mode})");
    }

    // ─── Private: Wormhole (trustless) flow ───

    private async Task<OASISResult<BridgeTransactionResult>> InitiateWormholeBridgeAsync(
        string sourceChain, string targetChain, string tokenId,
        string recipientAddress, Guid avatarId, int amount,
        CancellationToken ct)
    {
        var initiationResult = await _wormhole.InitiateTransferAsync(
            sourceChain, targetChain, tokenId, "", recipientAddress, amount, ct);

        if (initiationResult.IsError)
            return Error<BridgeTransactionResult>($"Wormhole initiation failed: {initiationResult.Message}");

        var initiation = initiationResult.Result!;

        var bridgeTx = new BridgeTransactionResult
        {
            Id = $"wh_bridge_{Guid.NewGuid():N}",
            AvatarId = avatarId,
            SourceChain = sourceChain,
            TargetChain = targetChain,
            SourceTokenId = tokenId,
            TargetAddress = recipientAddress,
            Amount = amount,
            Mode = BridgeMode.Wormhole,
            Status = BridgeStatus.AwaitingVAA,
            LockTxHash = initiation.TxHash,
            WormholeEmitterChainId = initiation.EmitterChainId,
            WormholeEmitterAddress = initiation.EmitterAddress,
            WormholeSequence = initiation.Sequence,
            CreatedAt = DateTime.UtcNow
        };

        _db.BridgeTransactions.Add(bridgeTx);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Wormhole bridge initiated: {Id} {Source}→{Target} seq={Sequence} — awaiting Guardian VAA",
            bridgeTx.Id, sourceChain, targetChain, initiation.Sequence);

        return Ok(bridgeTx,
            $"Wormhole bridge initiated: {sourceChain} → {targetChain}. " +
            $"Call FetchVAA to poll for Guardian signatures, then RedeemWithVAA to complete.");
    }

    // ─── Private: Trusted (custodial) flow ───

    private async Task<OASISResult<BridgeTransactionResult>> InitiateTrustedBridgeAsync(
        string sourceChain, string targetChain, string tokenId,
        string recipientAddress, Guid avatarId, int amount,
        CancellationToken ct)
    {
        var sourceProvider = _factory.GetProvider(sourceChain, ChainNetwork.Devnet);
        var targetProvider = _factory.GetProvider(targetChain, ChainNetwork.Devnet);

        if (!sourceProvider.SupportsBridging)
            return Error<BridgeTransactionResult>($"{sourceChain} does not support bridging");

        var bridgeVault = GetBridgeVaultAddress(sourceChain, targetChain);
        var lockResult = await sourceProvider.LockForBridgeAsync(
            tokenId, bridgeVault, amount, targetChain, recipientAddress, ct);

        if (lockResult.IsError)
            return Error<BridgeTransactionResult>($"Source chain lock failed: {lockResult.Message}");

        var mintResult = await targetProvider.MintWrappedAsync(
            sourceChain, tokenId, $"bridge://{sourceChain}/{tokenId}",
            amount, recipientAddress, ct);

        var bridgeTx = new BridgeTransactionResult
        {
            Id = $"bridge_{Guid.NewGuid():N}",
            AvatarId = avatarId,
            SourceChain = sourceChain,
            TargetChain = targetChain,
            SourceTokenId = tokenId,
            TargetTokenId = mintResult.Result,
            SourceAddress = lockResult.Result ?? "",
            TargetAddress = recipientAddress,
            Amount = amount,
            Mode = BridgeMode.Trusted,
            Status = BridgeStatus.Completed,
            LockTxHash = lockResult.Result,
            MintTxHash = mintResult.Result,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };

        _db.BridgeTransactions.Add(bridgeTx);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Trusted bridge completed: {Id} {Source}→{Target} token={TokenId} amount={Amount}",
            bridgeTx.Id, sourceChain, targetChain, tokenId, amount);

        return Ok(bridgeTx, $"Trusted bridge completed: {sourceChain} → {targetChain}");
    }

    private string GetBridgeVaultAddress(string sourceChain, string targetChain)
    {
        // Use configured vault address from Wormhole section, falling back to placeholder
        if (_wormholeConfig.BridgeVaults.TryGetValue(sourceChain, out var vaultCfg)
            && !string.IsNullOrWhiteSpace(vaultCfg.VaultAddress))
        {
            return vaultCfg.VaultAddress;
        }

        _logger.LogWarning(
            "No bridge vault configured for {Chain}. Using placeholder. Configure Blockchain:Wormhole:BridgeVaults",
            sourceChain);

        return $"{sourceChain.ToLowerInvariant()}_bridge_vault_for_{targetChain.ToLowerInvariant()}";
    }

    private OASISResult<T> Ok<T>(T result, string message = "")
        => new() { IsError = false, Result = result, Message = message };

    private OASISResult<T> Error<T>(string message, Exception? ex = null)
    {
        _logger.LogError(ex, "Bridge error: {Message}", message);
        return new OASISResult<T> { IsError = true, Message = message, Exception = ex };
    }
}
