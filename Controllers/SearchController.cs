using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly ISearchManager _searchManager;

    public SearchController(ISearchManager searchManager)
    {
        _searchManager = searchManager;
    }

    [HttpPost]
    public async Task<ActionResult<OASISResult<SearchResult>>> Search([FromBody] SearchRequest request, [FromQuery] OASISRequest? providerRequest)
    {
        var result = await _searchManager.SearchAsync(request, providerRequest);
        if (result.IsError) return BadRequest(result);
        return Ok(result);
    }

    [HttpGet("facets")]
    public async Task<ActionResult<OASISResult<List<SearchFacet>>>> GetFacets([FromQuery] OASISRequest? providerRequest)
    {
        var result = await _searchManager.GetFacetsAsync(providerRequest);
        if (result.IsError) return BadRequest(result);
        return Ok(result);
    }
}
