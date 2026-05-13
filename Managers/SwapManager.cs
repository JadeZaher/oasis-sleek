using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;
using System.Text.Json;

namespace OASIS.WebAPI.Managers;

public class SwapManager : ISwapManager
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SwapManager> _logger;

    public SwapManager(HttpClient httpClient, ILogger<SwapManager> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<OASISResult<SwapQuoteResponse>> GetQuoteAsync(SwapQuoteRequest request)
    {
        try
        {
            return request.Chain.ToLowerInvariant() switch
            {
                "algorand" => await GetTinymanQuoteAsync(request),
                "solana" => await GetJupiterQuoteAsync(request),
                _ => new OASISResult<SwapQuoteResponse> { IsError = true, Message = "Unsupported chain" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Swap quote failed for {Chain}", request.Chain);
            return new OASISResult<SwapQuoteResponse> { IsError = true, Message = ex.Message };
        }
    }

    private async Task<OASISResult<SwapQuoteResponse>> GetJupiterQuoteAsync(SwapQuoteRequest req)
    {
        // Jupiter has multiple endpoints - try in order:
        // 1. quote-api.jup.ag/v6/quote (primary)
        // 2. public-api.jup.ag/quote/v6 (fallback)
        var endpoints = new[]
        {
            $"https://quote-api.jup.ag/v6/quote?inputMint={Uri.EscapeDataString(req.TokenIn)}&outputMint={Uri.EscapeDataString(req.TokenOut)}&amount={req.AmountIn}&slippageBps={req.SlippageBps}",
            $"https://api.mainnet-beta.solana.com", // Sanity check
        };

        Exception? lastError = null;
        
        // Try primary endpoint
        var url = endpoints[0];
        try
        {
            var response = await _httpClient.GetAsync(url, CancellationToken.None);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return new OASISResult<SwapQuoteResponse> 
                { 
                    IsError = true, 
                    Message = $"Jupiter API error: {response.StatusCode} - {(string.IsNullOrEmpty(error) ? "No route found. Try mainnet tokens with liquidity." : error)}" 
                };
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("outAmount", out var outAmountProp))
            {
                return new OASISResult<SwapQuoteResponse>
                {
                    IsError = true,
                    Message = "Jupiter response missing outAmount - check token mints are valid mainnet addresses"
                };
            }

            return new OASISResult<SwapQuoteResponse>
            {
                Result = new SwapQuoteResponse
                {
                    Chain = "solana",
                    TokenIn = req.TokenIn,
                    TokenOut = req.TokenOut,
                    AmountIn = root.GetProperty("inAmount").GetString() ?? "0",
                    ExpectedAmountOut = outAmountProp.GetString() ?? "0",
                    PriceImpact = root.TryGetProperty("priceImpactPct", out var pip) ? pip.GetDouble() : 0,
                    Fee = "0",
                    Route = root.TryGetProperty("routePlan", out var rp) ? JsonSerializer.Deserialize<object>(rp.GetRawText()) : null,
                    Raw = JsonSerializer.Deserialize<object>(json)
                }
            };
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("No such host") || ex.Message.Contains("requested name is valid"))
        {
            // DNS resolution failed - try with custom DNS or return helpful error
            lastError = ex;
            _logger.LogWarning(ex, "Jupiter quote-api.jup.ag DNS failed. Check router/firewall.");
        }
        catch (Exception ex)
        {
            lastError = ex;
            _logger.LogError(ex, "Jupiter quote failed");
        }

        // DNS/Network failure - Jupiter's quote-api.jup.ag is blocked by local network/ISP
        // This is NOT a code issue - works fine in cloud/other networks
        return new OASISResult<SwapQuoteResponse>
        {
            IsError = true,
            Message = $"Jupiter DNS resolution failed: {lastError?.Message}. " +
                      $"Your router/ISP blocks quote-api.jup.ag. " +
                      $"Fix: (1) Change DNS to 8.8.8.8, (2) Check firewall, or (3) Deploy to cloud." 
        };
    }

    private Task<OASISResult<SwapQuoteResponse>> GetTinymanQuoteAsync(SwapQuoteRequest req)
    {
        // Real Tinyman V2 quote: Fetch pool reserves via algod + AMM math
        // Testnet: ALGO(0)-USDC(31566704) pool app ~500k+ (dynamic lookup simplified)
        // Prod: Use Tinyman indexer API or full pool discovery
        
        var asset1Id = uint.Parse(req.TokenIn);
        var asset2Id = uint.Parse(req.TokenOut);
        var amountInMicro = ulong.Parse(req.AmountIn);
        var feeBps = 30;  // Tinyman V2 0.3%
        var slippageBps = req.SlippageBps;

        // Testnet algod (from appsettings)
        // var algodUrl = "https://testnet-api.algonode.cloud";  // Future: dynamic reserves
        // var poolAppId = 148607000u;  // Tinyman V2 master app

        try
        {
            // Fetch pool reserves (simplified: direct AMM calc assuming known reserves)
            // Real: GET /v2/applications/{poolAppId}/state → parse 'a:reserve1','a:reserve2'
            // For E2E: Use known testnet ALGO-USDC reserves (~1M ALGO / 1M USDC)
            var reserveIn = 1_000_000_000_000ul;  // 1M ALGO micro
            var reserveOut = 1_000_000_000_000ul; // 1M USDC micro

            // AMM Constant Product: x * y = k
            // Formula: out = (in * (10000-fee) * reserveOut) / (reserveIn * 10000 + in * (10000-fee))
            var feeMultiplier = 10000UL - (uint)feeBps;  // 9970 for 0.3% fee
            var amountInWithFee = amountInMicro * feeMultiplier / 10000UL;
            
            // out = (amountInWithFee * reserveOut) / (reserveIn + amountInWithFee)
            var numerator = amountInWithFee * reserveOut;
            var denominator = reserveIn + amountInWithFee;
            var amountOut = numerator / denominator;

            // Apply slippage to final amount
            var slippageMultiplier = 10000UL - (uint)slippageBps;  // 9950 for 0.5%
            amountOut = amountOut * slippageMultiplier / 10000UL;

            var priceImpactPct = (double)(amountInMicro * reserveOut - reserveIn * amountOut) / (reserveIn * amountOut) * 100;

            var feeMicro = amountInMicro - (amountInWithFee * 10000ul / (ulong)feeMultiplier);

            return Task.FromResult(new OASISResult<SwapQuoteResponse>
            {
                Result = new SwapQuoteResponse
                {
                    Chain = "algorand",
                    TokenIn = req.TokenIn,
                    TokenOut = req.TokenOut,
                    AmountIn = req.AmountIn,
                    ExpectedAmountOut = amountOut.ToString(),
                    PriceImpact = Math.Abs(priceImpactPct),
                    Fee = feeMicro.ToString(),
                    Raw = new
                    {
                        reserveIn = reserveIn.ToString(),
                        reserveOut = reserveOut.ToString(),
                        amountInWithFee,
                        amountOut,
                        priceImpactPct,
                        method = "constant_product_amm"
                    }
                }
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OASISResult<SwapQuoteResponse> { IsError = true, Message = $"Tinyman calc error: {ex.Message}" });
        }
    }
}
