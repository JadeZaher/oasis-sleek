using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]  // Optional: quotas via avatar
public class SwapController : ControllerBase
{
    private readonly ISwapManager _swapManager;

    public SwapController(ISwapManager swapManager)
    {
        _swapManager = swapManager;
    }

    [HttpGet("quote")]
    public async Task<ActionResult<OASISResult<SwapQuoteResponse>>> GetQuote([FromQuery] SwapQuoteRequest request)
    {
        var result = await _swapManager.GetQuoteAsync(request);
        if (result.IsError) return BadRequest(result);
        return Ok(result);
    }
}
