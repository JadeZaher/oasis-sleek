using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Managers;

/// <summary>
/// Thin swap dispatcher. Validates the request, resolves the per-chain
/// <see cref="IDexAdapter"/> (Tinyman/Algorand, Jupiter/Solana, …) by
/// <see cref="SwapQuoteRequest.Chain"/>, and delegates. All chain-specific
/// HTTP / AMM math / pool discovery lives in the adapters.
///
/// SwapManager owns the cross-cutting quote→execute cache and the
/// <see cref="SwapQuoteResponse.QuoteId"/> lifecycle so behavior is uniform
/// across chains: an adapter returns a <see cref="DexQuote"/> (quote + opaque
/// payload); SwapManager assigns the QuoteId, caches the payload, and replays
/// it into the adapter on execute.
///
/// Adding a new chain = add one <see cref="IDexAdapter"/> implementation + one
/// DI registration. This dispatcher never changes.
/// </summary>
public class SwapManager : ISwapManager
{
    private readonly IReadOnlyDictionary<string, IDexAdapter> _adapters;
    private readonly ILogger<SwapManager> _logger;

    // In-memory quote cache keyed by QuoteId (valid for ~2 min). Cross-cutting
    // across chains, so it stays here rather than in any single adapter.
    private static readonly Dictionary<string, CachedQuote> _quoteCache = new();
    private static readonly object _cacheLock = new();

    public SwapManager(IEnumerable<IDexAdapter> adapters, ILogger<SwapManager> logger)
    {
        // Resolve adapters by chain, case-insensitive (mirrors the chain switch
        // that used request.Chain.ToLowerInvariant()).
        _adapters = adapters.ToDictionary(a => a.Chain, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<OASISResult<SwapQuoteResponse>> GetQuoteAsync(SwapQuoteRequest request)
    {
        try
        {
            if (!_adapters.TryGetValue(request.Chain, out var adapter))
                return new OASISResult<SwapQuoteResponse> { IsError = true, Message = "Unsupported chain" };

            var quoteResult = await adapter.GetQuoteAsync(request);
            if (quoteResult.IsError || quoteResult.Result is null)
                return new OASISResult<SwapQuoteResponse>
                {
                    IsError = true,
                    Message = quoteResult.Message,
                    Exception = quoteResult.Exception
                };

            var dexQuote = quoteResult.Result;
            var quote = dexQuote.Quote;

            // SwapManager owns the QuoteId + cache lifecycle (uniform across chains).
            quote.QuoteId = Guid.NewGuid().ToString("N");
            CacheQuote(quote.QuoteId, request.Chain, dexQuote.CachePayload);

            return new OASISResult<SwapQuoteResponse>
            {
                IsError = false,
                Result = quote,
                Message = quoteResult.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Swap quote failed for {Chain}", request.Chain);
            return new OASISResult<SwapQuoteResponse> { IsError = true, Message = ex.Message, Exception = ex };
        }
    }

    public async Task<OASISResult<SwapQuoteResponse>> GetSwapTransactionAsync(SwapExecuteRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.QuoteId))
                return Error<SwapQuoteResponse>("QuoteId is required");

            if (string.IsNullOrWhiteSpace(request.WalletAddress))
                return Error<SwapQuoteResponse>("WalletAddress is required for swap execution");

            if (!_adapters.TryGetValue(request.Chain, out var adapter))
                return Error<SwapQuoteResponse>($"Unsupported chain: {request.Chain}");

            if (!TryGetCachedQuote(request.QuoteId, out var cachedPayload))
                return Error<SwapQuoteResponse>("Quote expired or not found. Request a new quote first.");

            return await adapter.BuildSwapTransactionAsync(request, cachedPayload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Swap transaction build failed for {Chain}", request.Chain);
            return Error<SwapQuoteResponse>(ex.Message, ex);
        }
    }

    // ─── Quote Cache (simple in-memory, for bridging quote → execute) ───

    private static void CacheQuote(string quoteId, string chain, string rawPayload)
    {
        lock (_cacheLock)
        {
            _quoteCache[quoteId] = new CachedQuote
            {
                Chain = chain,
                RawPayload = rawPayload,
                ExpiresAt = DateTime.UtcNow.AddMinutes(2)
            };

            // Cleanup expired entries
            var expired = _quoteCache.Where(kv => kv.Value.ExpiresAt < DateTime.UtcNow)
                .Select(kv => kv.Key).ToList();
            foreach (var key in expired)
                _quoteCache.Remove(key);
        }
    }

    private static bool TryGetCachedQuote(string quoteId, out string rawPayload)
    {
        lock (_cacheLock)
        {
            if (_quoteCache.TryGetValue(quoteId, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
            {
                rawPayload = cached.RawPayload;
                return true;
            }
            rawPayload = "";
            return false;
        }
    }

    private sealed class CachedQuote
    {
        public string Chain { get; set; } = "";
        public string RawPayload { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
    }

    // ─── Result helpers ───

    private static OASISResult<T> Error<T>(string message, Exception? ex = null)
        => new() { IsError = true, Message = message, Exception = ex };
}
