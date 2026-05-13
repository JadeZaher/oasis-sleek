using System.ComponentModel.DataAnnotations;

namespace OASIS.WebAPI.Models.Requests;

public class SwapQuoteRequest
{
    [Required]
    public string Chain { get; set; } = string.Empty;  // "algorand" | "solana"

    [Required]
    public string TokenIn { get; set; } = string.Empty;

    [Required]
    public string TokenOut { get; set; } = string.Empty;

    [Required]
    public string AmountIn { get; set; } = string.Empty;

    public int SlippageBps { get; set; } = 50;
}
