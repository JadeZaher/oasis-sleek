using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.Managers;

public interface ISearchManager
{
    Task<OASISResult<SearchResult>> SearchAsync(SearchRequest request, OASISRequest? providerRequest = null);
    Task<OASISResult<List<SearchFacet>>> GetFacetsAsync(OASISRequest? providerRequest = null);
}
