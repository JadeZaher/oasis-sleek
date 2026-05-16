using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OASIS.WebAPI.Core.Blockchain.Wormhole;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Controllers;

/// <summary>
/// Cross-chain bridge endpoints supporting both trusted (custodial)
/// and trustless (Wormhole) bridging modes.
///
/// Trusted flow:  POST /initiate → Completed immediately
/// Wormhole flow: POST /initiate → POST /{id}/fetch-vaa → POST /{id}/redeem
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BridgeController : ControllerBase
{
    private readonly ICrossChainBridgeService _bridgeService;
    private readonly ILogger<BridgeController> _logger;

    public BridgeController(
        ICrossChainBridgeService bridgeService,
        ILogger<BridgeController> logger)
    {
        _bridgeService = bridgeService;
        _logger = logger;
    }

    /// <summary>
    /// Get all supported bridge routes between chains (including Wormhole availability).
    /// </summary>
    [HttpGet("routes")]
    [ProducesResponseType(typeof(IEnumerable<BridgeRouteInfo>), 200)]
    public async Task<IActionResult> GetRoutes(CancellationToken ct)
    {
        var result = await _bridgeService.GetSupportedRoutesAsync(ct);
        if (result.IsError)
            return BadRequest(result.ToErrorPayload());

        return Ok(result.Result);
    }

    /// <summary>
    /// Initiate a cross-chain bridge.
    /// Set mode to "Wormhole" for trustless bridging (requires follow-up VAA fetch + redeem).
    /// Defaults to the server-configured default mode.
    /// </summary>
    [HttpPost("initiate")]
    [ProducesResponseType(typeof(BridgeTransactionResult), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> InitiateBridge(
        [FromBody] BridgeInitiateRequest request,
        CancellationToken ct)
    {
        var avatarId = GetAvatarId();

        var result = await _bridgeService.InitiateBridgeAsync(
            request.SourceChain, request.TargetChain, request.TokenId,
            request.RecipientAddress, avatarId, request.Amount,
            request.Mode, ct);

        if (result.IsError)
            return BadRequest(result.ToErrorPayload());

        return Ok(result.Result);
    }

    /// <summary>
    /// Fetch the signed VAA from the Wormhole Guardian network.
    /// Call this after initiating a Wormhole bridge to poll for Guardian consensus.
    /// </summary>
    [HttpPost("{id}/fetch-vaa")]
    [ProducesResponseType(typeof(BridgeTransactionResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> FetchVAA(string id, CancellationToken ct)
    {
        var result = await _bridgeService.FetchVAAAsync(id, ct);
        if (result.IsError)
        {
            if (result.Message.Contains("not found"))
                return NotFound(result.ToErrorPayload());
            return BadRequest(result.ToErrorPayload());
        }

        return Ok(result.Result);
    }

    /// <summary>
    /// Redeem a Wormhole bridge on the target chain.
    /// Submits the verified VAA to the target chain's Token Bridge to complete
    /// the trustless transfer. The VAA must have been fetched first.
    /// </summary>
    [HttpPost("{id}/redeem")]
    [ProducesResponseType(typeof(BridgeTransactionResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RedeemWithVAA(string id, CancellationToken ct)
    {
        var result = await _bridgeService.RedeemWithVAAAsync(id, ct);
        if (result.IsError)
        {
            if (result.Message.Contains("not found"))
                return NotFound(result.ToErrorPayload());
            return BadRequest(result.ToErrorPayload());
        }

        return Ok(result.Result);
    }

    /// <summary>
    /// Mark a bridge transaction as completed (trusted mode).
    /// </summary>
    [HttpPost("{id}/complete")]
    [ProducesResponseType(typeof(BridgeTransactionResult), 200)]
    public async Task<IActionResult> CompleteBridge(string id, CancellationToken ct)
    {
        var result = await _bridgeService.CompleteBridgeAsync(id, ct);
        if (result.IsError)
            return NotFound(result.ToErrorPayload());

        return Ok(result.Result);
    }

    /// <summary>
    /// Reverse a completed bridge: burn wrapped, release original.
    /// </summary>
    [HttpPost("{id}/reverse")]
    [ProducesResponseType(typeof(BridgeTransactionResult), 200)]
    public async Task<IActionResult> ReverseBridge(
        string id,
        [FromBody] BridgeReverseRequest request,
        CancellationToken ct)
    {
        var result = await _bridgeService.ReverseBridgeAsync(id, request.SourceRecipientAddress, ct);
        if (result.IsError)
            return BadRequest(result.ToErrorPayload());

        return Ok(result.Result);
    }

    /// <summary>
    /// Get status of a specific bridge transaction.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(BridgeTransactionResult), 200)]
    public async Task<IActionResult> GetBridgeStatus(string id, CancellationToken ct)
    {
        var result = await _bridgeService.GetBridgeStatusAsync(id, ct);
        if (result.IsError)
            return NotFound(result.ToErrorPayload());

        return Ok(result.Result);
    }

    /// <summary>
    /// Get bridge history for the authenticated avatar.
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(IEnumerable<BridgeTransactionResult>), 200)]
    public async Task<IActionResult> GetHistory(CancellationToken ct)
    {
        var avatarId = GetAvatarId();
        var result = await _bridgeService.GetBridgeHistoryAsync(avatarId, ct);
        if (result.IsError)
            return BadRequest(result.ToErrorPayload());

        return Ok(result.Result);
    }

    private Guid GetAvatarId()
    {
        var avatarClaim = User.FindFirst("avatarId")?.Value;
        if (Guid.TryParse(avatarClaim, out var avatarId))
            return avatarId;

        // Fallback: try NameIdentifier claim (sub)
        var subClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value;
        if (Guid.TryParse(subClaim, out var subId))
            return subId;

        // Last resort: derive a deterministic GUID from the identity name
        var userId = User.Identity?.Name
                  ?? User.FindFirst("client_id")?.Value
                  ?? "anonymous";
        var bytes = System.Text.Encoding.UTF8.GetBytes(userId);
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, Math.Min(bytes.Length, 16));
        return new Guid(guidBytes);
    }
}

// ─── Request DTOs ───

public class BridgeInitiateRequest
{
    public string SourceChain { get; set; } = string.Empty;
    public string TargetChain { get; set; } = string.Empty;
    public string TokenId { get; set; } = string.Empty;
    public string RecipientAddress { get; set; } = string.Empty;
    public int Amount { get; set; } = 1;

    /// <summary>
    /// Bridge mode: null = server default, "Trusted" = custodial, "Wormhole" = trustless.
    /// </summary>
    public BridgeMode? Mode { get; set; }
}

public class BridgeReverseRequest
{
    public string SourceRecipientAddress { get; set; } = string.Empty;
}
