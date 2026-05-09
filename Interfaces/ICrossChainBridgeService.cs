using OASIS.WebAPI.Core.Blockchain.Wormhole;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces;

/// <summary>
/// Hybrid cross-chain bridge orchestrator supporting both trusted (custodial)
/// and trustless (Wormhole) bridge modes.
/// </summary>
public interface ICrossChainBridgeService
{
    /// <summary>
    /// Initiate a bridge: lock asset on source chain and mint wrapped on target.
    /// When mode is Wormhole, the transfer pauses at AwaitingVAA until the client
    /// calls RedeemWithVAAAsync to complete trustlessly.
    /// </summary>
    Task<OASISResult<BridgeTransactionResult>> InitiateBridgeAsync(
        string sourceChain, string targetChain, string tokenId,
        string recipientAddress, Guid avatarId, int amount = 1,
        BridgeMode? mode = null, CancellationToken ct = default);

    /// <summary>
    /// Complete a bridge by confirming the target-chain mint (trusted mode).
    /// </summary>
    Task<OASISResult<BridgeTransactionResult>> CompleteBridgeAsync(
        string bridgeTransactionId, CancellationToken ct = default);

    /// <summary>
    /// Fetch the signed VAA for a Wormhole bridge transaction.
    /// Polls the Guardian network until the VAA is available or timeout.
    /// </summary>
    Task<OASISResult<BridgeTransactionResult>> FetchVAAAsync(
        string bridgeTransactionId, CancellationToken ct = default);

    /// <summary>
    /// Redeem a Wormhole bridge on the target chain using a verified VAA.
    /// Completes the trustless transfer by submitting the VAA to the target chain.
    /// </summary>
    Task<OASISResult<BridgeTransactionResult>> RedeemWithVAAAsync(
        string bridgeTransactionId, CancellationToken ct = default);

    /// <summary>
    /// Reverse bridge: burn wrapped on target, release original on source.
    /// </summary>
    Task<OASISResult<BridgeTransactionResult>> ReverseBridgeAsync(
        string bridgeTransactionId, string sourceRecipientAddress, CancellationToken ct = default);

    /// <summary>
    /// Get bridge history for an avatar.
    /// </summary>
    Task<OASISResult<IEnumerable<BridgeTransactionResult>>> GetBridgeHistoryAsync(
        Guid avatarId, CancellationToken ct = default);

    /// <summary>
    /// Get all supported bridge routes (including Wormhole availability).
    /// </summary>
    Task<OASISResult<IEnumerable<BridgeRouteInfo>>> GetSupportedRoutesAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Get the status of a specific bridge transaction.
    /// </summary>
    Task<OASISResult<BridgeTransactionResult>> GetBridgeStatusAsync(
        string bridgeTransactionId, CancellationToken ct = default);
}
