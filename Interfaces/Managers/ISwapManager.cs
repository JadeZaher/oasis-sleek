using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.Managers;

public interface ISwapManager
{
    Task<OASISResult<SwapQuoteResponse>> GetQuoteAsync(SwapQuoteRequest request);
}
