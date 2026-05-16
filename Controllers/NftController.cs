using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NftController : ControllerBase
{
    private readonly INftManager _nftManager;

    public NftController(INftManager nftManager)
    {
        _nftManager = nftManager;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OASISResult<NftResult>>> Get(Guid id, [FromQuery] OASISRequest? request)
    {
        var result = await _nftManager.GetAsync(id, request);
        if (result.IsError || result.Result == null) return NotFound(result);

        var nftResult = MapToNftResult(result.Result);
        return Ok(new OASISResult<NftResult> { Result = nftResult, Message = result.Message });
    }

    [HttpGet]
    public async Task<ActionResult<OASISResult<IEnumerable<NftResult>>>> Query([FromQuery] NftQueryRequest query, [FromQuery] OASISRequest? request)
    {
        var result = await _nftManager.QueryAsync(query, request);
        if (result.IsError || result.Result == null) return Ok(new OASISResult<IEnumerable<NftResult>> { IsError = true, Message = result.Message });

        var mapped = result.Result.Select(MapToNftResult).ToList();
        return Ok(new OASISResult<IEnumerable<NftResult>> { Result = mapped, Message = "Success" });
    }

    [HttpPost("mint")]
    public async Task<ActionResult<OASISResult<IBlockchainOperation>>> Mint([FromBody] NftMintRequest request, [FromQuery] OASISRequest? providerRequest)
    {
        var avatarId = GetAvatarIdFromClaims();
        if (avatarId == null)
            return Unauthorized(new OASISResult<IBlockchainOperation> { IsError = true, Message = "Invalid token." });

        var result = await _nftManager.MintAsync(request, avatarId.Value, providerRequest);
        if (result.IsError) return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("{id:guid}/transfer")]
    public async Task<ActionResult<OASISResult<IBlockchainOperation>>> Transfer(Guid id, [FromBody] NftTransferRequest request, [FromQuery] OASISRequest? providerRequest)
    {
        var avatarId = GetAvatarIdFromClaims();
        if (avatarId == null)
            return Unauthorized(new OASISResult<IBlockchainOperation> { IsError = true, Message = "Invalid token." });

        var result = await _nftManager.TransferAsync(id, request, avatarId.Value, providerRequest);
        if (result.IsError) return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("{id:guid}/burn")]
    public async Task<ActionResult<OASISResult<IBlockchainOperation>>> Burn(Guid id, [FromBody] NftBurnRequest request, [FromQuery] OASISRequest? providerRequest)
    {
        var avatarId = GetAvatarIdFromClaims();
        if (avatarId == null)
            return Unauthorized(new OASISResult<IBlockchainOperation> { IsError = true, Message = "Invalid token." });

        var result = await _nftManager.BurnAsync(id, request.WalletId, avatarId.Value, providerRequest);
        if (result.IsError) return BadRequest(result);
        return Ok(result);
    }

    [HttpGet("{id:guid}/metadata")]
    [AllowAnonymous]
    public async Task<ActionResult<OASISResult<NftMetadata>>> GetMetadata(Guid id, [FromQuery] OASISRequest? request)
    {
        var result = await _nftManager.GetMetadataAsync(id, request);
        if (result.IsError || result.Result == null) return NotFound(result);
        return Ok(result);
    }

    private NftResult MapToNftResult(INft holon)
    {
        var metadata = new NftMetadata
        {
            Name = holon.Name,
            Description = holon.Description
        };

        if (holon.Metadata != null)
        {
            if (holon.Metadata.TryGetValue("image", out var image)) metadata.Image = image;
            if (holon.Metadata.TryGetValue("external_url", out var extUrl)) metadata.ExternalUrl = extUrl;
            if (holon.Metadata.TryGetValue("animation_url", out var animUrl)) metadata.AnimationUrl = animUrl;
        }

        return new NftResult
        {
            Id = holon.Id,
            Name = holon.Name,
            Description = holon.Description,
            OwnerAvatarId = holon.AvatarId,
            ChainId = holon.ChainId ?? string.Empty,
            TokenId = holon.TokenId,
            Metadata = metadata,
            CreatedDate = holon.CreatedDate,
            ModifiedDate = holon.ModifiedDate,
            IsActive = holon.IsActive
        };
    }

    private Guid? GetAvatarIdFromClaims()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                 ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
