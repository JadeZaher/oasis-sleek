namespace OASIS.WebAPI.Models.Responses;

public class SwapQuoteResponse
{
    public string Chain { get; set; } = string.Empty;
    public string TokenIn { get; set; } = string.Empty;
    public string TokenOut { get; set; } = string.Empty;
    public string AmountIn { get; set; } = string.Empty;
    public string ExpectedAmountOut { get; set; } = string.Empty;
    public double PriceImpact { get; set; }
    public string Fee { get; set; } = "0";
    public object? Route { get; set; }
    public object? Raw { get; set; }

    /// <summary>Unique quote identifier for downstream swap execution (Jupiter v2).</summary>
    public string? QuoteId { get; set; }

    /// <summary>Base64-encoded unsigned swap transaction for client-side signing (Jupiter v2 /swap).</summary>
    public string? SwapTransaction { get; set; }

    /// <summary>Last valid block height for the swap transaction (Solana).</summary>
    public long? LastValidBlockHeight { get; set; }

    /// <summary>Human-readable status message.</summary>
    public string? Message { get; set; }
}
