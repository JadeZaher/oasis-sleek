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
}
