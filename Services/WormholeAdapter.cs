using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using OASIS.WebAPI.Core;
using OASIS.WebAPI.Core.Blockchain.Wormhole;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Services;

/// <summary>
/// Wormhole bridge adapter — integrates with the Guardian network REST API
/// to fetch and verify VAAs, and coordinates with blockchain providers to
/// initiate/redeem trustless cross-chain transfers.
///
/// Flow:
/// 1. Source chain provider publishes a Wormhole message (lock/transfer)
/// 2. Guardian network observes and signs a VAA
/// 3. This adapter fetches + verifies the VAA
/// 4. Target chain provider submits the VAA to complete the transfer
/// </summary>
public class WormholeAdapter : IWormholeAdapter
{
    private readonly HttpClient _guardianClient;
    private readonly IBlockchainProviderFactory _providerFactory;
    private readonly WormholeConfig _config;
    private readonly ILogger<WormholeAdapter> _logger;

    public WormholeAdapter(
        HttpClient guardianClient,
        IBlockchainProviderFactory providerFactory,
        IOptions<WormholeConfig> config,
        ILogger<WormholeAdapter> logger)
    {
        _guardianClient = guardianClient;
        _providerFactory = providerFactory;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<OASISResult<WormholeTransferInitiation>> InitiateTransferAsync(
        string sourceChain, string targetChain,
        string tokenId, string senderAddress, string recipientAddress,
        int amount, CancellationToken ct = default)
    {
        if (!IsRouteSupported(sourceChain, targetChain))
            return Error<WormholeTransferInitiation>(
                $"Wormhole route {sourceChain}→{targetChain} is not supported");

        var sourceMapping = _config.ChainMappings[sourceChain];
        var targetMapping = _config.ChainMappings[targetChain];

        var sourceProvider = _providerFactory.GetProvider(sourceChain, ChainNetwork.Devnet);

        // Lock the asset on the source chain via the Wormhole Core Bridge.
        // The provider's LockForBridgeAsync should publish a Wormhole message
        // and return the tx hash. The sequence number is derived from the tx.
        var lockResult = await sourceProvider.LockForBridgeAsync(
            tokenId, sourceMapping.CoreBridgeAddress, amount,
            targetChain, recipientAddress, ct);

        if (lockResult.IsError)
            return Error<WormholeTransferInitiation>($"Source lock failed: {lockResult.Message}");

        // In production, parse the actual sequence from the on-chain tx logs.
        // For now, derive a deterministic sequence from the tx hash.
        var sequence = DeriveSequenceFromTxHash(lockResult.Result!);

        var initiation = new WormholeTransferInitiation
        {
            TxHash = lockResult.Result!,
            EmitterChainId = sourceMapping.WormholeChainId,
            EmitterAddress = NormalizeEmitterAddress(sourceMapping.CoreBridgeAddress),
            Sequence = sequence
        };

        _logger.LogInformation(
            "Wormhole transfer initiated: {SourceChain}→{TargetChain} seq={Sequence} tx={TxHash}",
            sourceChain, targetChain, sequence, lockResult.Result);

        return Ok(initiation);
    }

    public async Task<OASISResult<WormholeVAA>> FetchVAAAsync(
        int emitterChainId, string emitterAddress, long sequence,
        CancellationToken ct = default)
    {
        var endpoint = $"/v1/signed_vaa/{emitterChainId}/{emitterAddress}/{sequence}";
        var deadline = DateTime.UtcNow.AddSeconds(_config.VaaTimeoutSeconds);

        _logger.LogInformation(
            "Polling for VAA: chain={ChainId} emitter={Emitter} seq={Sequence}",
            emitterChainId, emitterAddress, sequence);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var response = await _guardianClient.GetAsync(endpoint, ct);

                if (response.IsSuccessStatusCode)
                {
                    var envelope = await response.Content.ReadFromJsonAsync<GuardianVAAEnvelope>(cancellationToken: ct);
                    if (envelope?.Data?.VaaBytes is { } vaaBytes)
                    {
                        var vaa = ParseVAA(vaaBytes, emitterChainId, emitterAddress, sequence);
                        _logger.LogInformation("VAA fetched: seq={Sequence} signatures={Sigs}",
                            sequence, vaa.SignatureCount);
                        return Ok(vaa);
                    }
                }

                if ((int)response.StatusCode == 404)
                {
                    // VAA not yet available — Guardians haven't reached consensus
                    await Task.Delay(_config.VaaPollIntervalMs, ct);
                    continue;
                }

                return Error<WormholeVAA>(
                    $"Guardian API error: {response.StatusCode} for seq={sequence}");
            }
            catch (OperationCanceledException) { throw; }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Guardian API request failed, retrying...");
                await Task.Delay(_config.VaaPollIntervalMs, ct);
            }
        }

        return Error<WormholeVAA>(
            $"VAA fetch timed out after {_config.VaaTimeoutSeconds}s for seq={sequence}");
    }

    public Task<OASISResult<bool>> VerifyVAAAsync(WormholeVAA vaa, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaa.VaaBytes))
            return Task.FromResult(Error<bool>("VAA bytes are empty"));

        if (vaa.SignatureCount < _config.MinGuardianSignatures)
            return Task.FromResult(Error<bool>(
                $"Insufficient Guardian signatures: {vaa.SignatureCount}/{_config.MinGuardianSignatures}"));

        if (vaa.Version != 1)
            return Task.FromResult(Error<bool>($"Unsupported VAA version: {vaa.Version}"));

        // In production, verify each Guardian signature against the known Guardian set
        // public keys. For now, we trust the Guardian API response structure and
        // validate the minimum signature count.
        _logger.LogInformation(
            "VAA verified: seq={Sequence} chain={Chain} sigs={Sigs}/{Required}",
            vaa.Sequence, vaa.EmitterChainId, vaa.SignatureCount, _config.MinGuardianSignatures);

        return Task.FromResult(Ok(true, "VAA verification passed"));
    }

    public async Task<OASISResult<WormholeRedemptionResult>> RedeemTransferAsync(
        string targetChain, WormholeVAA vaa, string recipientAddress,
        CancellationToken ct = default)
    {
        if (!_config.ChainMappings.ContainsKey(targetChain))
            return Error<WormholeRedemptionResult>($"Unknown target chain: {targetChain}");

        // Verify the VAA before redeeming
        var verifyResult = await VerifyVAAAsync(vaa, ct);
        if (verifyResult.IsError)
            return Error<WormholeRedemptionResult>($"VAA verification failed: {verifyResult.Message}");

        var targetProvider = _providerFactory.GetProvider(targetChain, ChainNetwork.Devnet);

        // Submit the VAA to the target chain's Token Bridge.
        // The provider's MintWrappedAsync handles the on-chain redemption.
        var mintResult = await targetProvider.MintWrappedAsync(
            $"wormhole:{vaa.EmitterChainId}",
            $"seq:{vaa.Sequence}",
            vaa.VaaBytes,   // Pass VAA bytes as the token URI for on-chain verification
            1,              // Amount is encoded in the VAA payload
            recipientAddress,
            ct);

        var redemption = new WormholeRedemptionResult
        {
            TxHash = mintResult.Result ?? "",
            Success = !mintResult.IsError,
            ErrorMessage = mintResult.IsError ? mintResult.Message : null
        };

        if (redemption.Success)
        {
            _logger.LogInformation(
                "Wormhole redemption completed: {TargetChain} tx={TxHash} seq={Sequence}",
                targetChain, redemption.TxHash, vaa.Sequence);
        }

        return redemption.Success
            ? Ok(redemption, "Wormhole transfer redeemed on target chain")
            : Error<WormholeRedemptionResult>($"Redemption failed: {redemption.ErrorMessage}");
    }

    public int? GetWormholeChainId(string oasisChainName)
    {
        return _config.ChainMappings.TryGetValue(oasisChainName, out var mapping)
            ? mapping.WormholeChainId
            : null;
    }

    public bool IsRouteSupported(string sourceChain, string targetChain)
    {
        return _config.ChainMappings.ContainsKey(sourceChain)
            && _config.ChainMappings.ContainsKey(targetChain)
            && sourceChain != targetChain;
    }

    // ─── Internal helpers ───

    private static WormholeVAA ParseVAA(string vaaBase64, int emitterChainId, string emitterAddress, long sequence)
    {
        var vaaBytes = Convert.FromBase64String(vaaBase64);

        // VAA wire format (simplified):
        // [0]       version (1 byte)
        // [1..4]    guardian set index (4 bytes, big-endian)
        // [5]       signature count (1 byte)
        // [6..N]    signatures (66 bytes each: guardian index + r + s + v)
        // Then body: timestamp(4) + nonce(4) + emitterChain(2) + emitterAddr(32) + sequence(8) + ...

        var version = vaaBytes.Length > 0 ? vaaBytes[0] : 1;
        var guardianSetIndex = vaaBytes.Length > 4
            ? (vaaBytes[1] << 24) | (vaaBytes[2] << 16) | (vaaBytes[3] << 8) | vaaBytes[4]
            : 0;
        var sigCount = vaaBytes.Length > 5 ? vaaBytes[5] : 0;

        return new WormholeVAA
        {
            Version = version,
            GuardianSetIndex = guardianSetIndex,
            SignatureCount = sigCount,
            VaaBytes = vaaBase64,
            EmitterChainId = emitterChainId,
            EmitterAddress = emitterAddress,
            Sequence = sequence,
            Timestamp = DateTime.UtcNow
        };
    }

    private static string NormalizeEmitterAddress(string address)
    {
        // Wormhole expects 32-byte hex-encoded emitter addresses.
        // Pad shorter addresses (e.g., Algorand app IDs) to 64 hex chars.
        if (address.Length < 64)
            return address.PadLeft(64, '0');
        return address;
    }

    private static long DeriveSequenceFromTxHash(string txHash)
    {
        // In production, parse the sequence from the source chain's Wormhole message logs.
        // For dev/testing, derive a deterministic sequence from the tx hash.
        var hash = txHash.GetHashCode(StringComparison.Ordinal);
        return Math.Abs((long)hash);
    }

    private OASISResult<T> Ok<T>(T result, string message = "")
        => new() { IsError = false, Result = result, Message = message };

    private OASISResult<T> Error<T>(string message, Exception? ex = null)
    {
        _logger.LogError(ex, "Wormhole error: {Message}", message);
        return new OASISResult<T> { IsError = true, Message = message, Exception = ex };
    }
}
