using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OASIS.WebAPI.Core;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WalletController : ControllerBase
{
    private readonly IWalletManager _walletManager;

    public WalletController(IWalletManager walletManager)
    {
        _walletManager = walletManager;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OASISResult<IWallet>>> Get(Guid id, [FromQuery] OASISRequest? request)
    {
        var result = await _walletManager.GetAsync(id, request);
        if (result.IsError || result.Result == null) return NotFound(result);
        return Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<OASISResult<IEnumerable<IWallet>>>> Query([FromQuery] WalletQueryRequest query, [FromQuery] OASISRequest? request)
    {
        var result = await _walletManager.QueryAsync(query, request);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<OASISResult<IWallet>>> Create([FromBody] WalletCreateModel model, [FromQuery] OASISRequest? request)
    {
        var avatarId = GetAvatarIdFromClaims();
        if (avatarId == null)
            return Unauthorized(new OASISResult<IWallet> { IsError = true, Message = "Invalid token." });

        var result = await _walletManager.CreateAsync(model, avatarId.Value, request);
        if (result.IsError) return BadRequest(result);
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OASISResult<IWallet>>> Update(Guid id, [FromBody] WalletUpdateModel model, [FromQuery] OASISRequest? request)
    {
        var result = await _walletManager.UpdateAsync(id, model, request);
        if (result.IsError) return BadRequest(result);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<OASISResponse>> Delete(Guid id, [FromQuery] OASISRequest? request)
    {
        var result = await _walletManager.DeleteAsync(id, request);
        if (result.IsError || !result.Result) return NotFound(result);
        return Ok(new OASISResponse { Message = "Wallet deleted." });
    }

    [HttpPost("{id:guid}/set-default")]
    public async Task<ActionResult<OASISResult<bool>>> SetDefault(Guid id, [FromQuery] OASISRequest? request)
    {
        var avatarId = GetAvatarIdFromClaims();
        if (avatarId == null)
            return Unauthorized(new OASISResult<bool> { IsError = true, Message = "Invalid token." });

        var result = await _walletManager.SetDefaultAsync(avatarId.Value, id, request);
        if (result.IsError) return BadRequest(result);
        return Ok(result);
    }

    [HttpGet("{id:guid}/portfolio")]
    public async Task<ActionResult<OASISResult<PortfolioResult>>> GetPortfolio(Guid id, [FromQuery] OASISRequest? request)
    {
        var result = await _walletManager.GetPortfolioAsync(id, request);
        if (result.IsError) return NotFound(result);
        return Ok(result);
    }

    // ─── Generate a new wallet on-platform ───

    [HttpPost("generate")]
    public async Task<ActionResult<OASISResult<IWallet>>> Generate([FromBody] WalletGenerateRequest model, [FromQuery] OASISRequest? request)
    {
        var avatarId = GetAvatarIdFromClaims();
        if (avatarId == null)
            return Unauthorized(new OASISResult<IWallet> { IsError = true, Message = "Invalid token." });

        var result = await _walletManager.GenerateWalletAsync(model, avatarId.Value, request);
        if (result.IsError) return BadRequest(result);
        return Ok(result);
    }

    // ─── Connect an external wallet (MetaMask, Ghost, etc.) ───

    [HttpPost("connect")]
    public async Task<ActionResult<OASISResult<IWallet>>> Connect([FromBody] WalletConnectRequest model, [FromQuery] OASISRequest? request)
    {
        var avatarId = GetAvatarIdFromClaims();
        if (avatarId == null)
            return Unauthorized(new OASISResult<IWallet> { IsError = true, Message = "Invalid token." });

        var result = await _walletManager.ConnectWalletAsync(model, avatarId.Value, request);
        if (result.IsError) return BadRequest(result);
        return Ok(result);
    }

    // ─── Export a platform wallet's private key ───

    [HttpPost("{id:guid}/export")]
    public async Task<ActionResult<OASISResult<WalletExportResult>>> Export(Guid id, [FromQuery] OASISRequest? request)
    {
        var avatarId = GetAvatarIdFromClaims();
        if (avatarId == null)
            return Unauthorized(new OASISResult<WalletExportResult> { IsError = true, Message = "Invalid token." });

        var result = await _walletManager.ExportWalletAsync(id, avatarId.Value, request);
        if (result.IsError) return BadRequest(result);
        return Ok(result);
    }

    // ─── Top-up a wallet with test tokens (faucet) — dev / test networks only ───

    [HttpPost("{id:guid}/topup")]
    public async Task<ActionResult<OASISResult<object>>> TopUp(Guid id, [FromBody] WalletTopUpRequest? model, [FromQuery] OASISRequest? request)
    {
        var avatarId = GetAvatarIdFromClaims();
        if (avatarId == null)
            return Unauthorized(new OASISResult<object> { IsError = true, Message = "Invalid token." });

        var result = await _walletManager.TopUpAsync(id, model?.Amount, avatarId.Value, request);
        if (result.IsError) return BadRequest(result);
        return Ok(result);
    }

    // ─── Get all wallets grouped by type (for UI) ───

    [HttpGet("types")]
    public async Task<ActionResult<OASISResult<object>>> GetByType([FromQuery] OASISRequest? request)
    {
        var avatarId = GetAvatarIdFromClaims();
        if (avatarId == null)
            return Unauthorized(new OASISResult<object> { IsError = true, Message = "Invalid token." });

        var allResult = await _walletManager.QueryAsync(new WalletQueryRequest { AvatarId = avatarId }, request);
        if (allResult.IsError || allResult.Result == null)
            return Ok(new OASISResult<object> { Result = new { external = new List<IWallet>(), platform = new List<IWallet>() } });

        var all = allResult.Result.ToList();
        var external = all.Where(w => w.WalletType == WalletType.External).ToList();
        var platform = all.Where(w => w.WalletType == WalletType.Platform).ToList();

        return Ok(new OASISResult<object>
        {
            Result = new { external, platform, total = all.Count },
            Message = "Wallets grouped by type."
        });
    }

    private Guid? GetAvatarIdFromClaims()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                 ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
