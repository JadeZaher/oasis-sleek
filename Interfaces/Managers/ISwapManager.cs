using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.Managers;

public interface ISwapManager
{
    Task<OASISResult<SwapQuoteResponse>> GetQuoteAsync(SwapQuoteRequest request);

    /// <summary>
    /// Build a transaction for executing a swap (Jupiter v2 /swap or Tinyman).
    /// Returns an unsigned transaction payload for client-side signing.
    /// </summary>
    Task<OASISResult<SwapQuoteResponse>> GetSwapTransactionAsync(SwapExecuteRequest request);
}
