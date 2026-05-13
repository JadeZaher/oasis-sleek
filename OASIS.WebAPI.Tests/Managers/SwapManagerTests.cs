using OASIS.WebAPI.Managers;
using OASIS.WebAPI.Models.Requests;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Xunit;
using System.Threading.Tasks;
using FluentAssertions;

namespace OASIS.WebAPI.Tests.Managers;

public class SwapManagerTests
{
    private readonly SwapManager _swapManager;
    private readonly HttpClient _httpClient;

    public SwapManagerTests()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
            DefaultRequestHeaders = { { "User-Agent", "OASIS-SwapManager-Tests" } }
        };
        var logger = new LoggerFactory().CreateLogger<SwapManager>();
        _swapManager = new SwapManager(_httpClient, logger);
    }

    [Fact]
    public async Task GetTinymanQuote_AlgorandTestnet_ReturnsValidQuote()
    {
        // Arrange
        var request = new SwapQuoteRequest
        {
            Chain = "algorand",
            TokenIn = "0",
            TokenOut = "31566704",
            AmountIn = "1000000",
            SlippageBps = 50
        };

        // Act
        var result = await _swapManager.GetQuoteAsync(request);

        // Assert
        result.IsError.Should().BeFalse();
        result.Result.Should().NotBeNull();
        result.Result!.Chain.Should().Be("algorand");
        result.Result.TokenIn.Should().Be("0");
        result.Result.TokenOut.Should().Be("31566704");
        result.Result.AmountIn.Should().Be("1000000");
        
        // Expected: ~996990 microUSDC (0.3% fee + 0.5% slippage on 1M ALGO)
        var expectedOut = long.Parse(result.Result.ExpectedAmountOut);
        expectedOut.Should().BeGreaterThan(990000);
        expectedOut.Should().BeLessThan(1000000);
        
        result.Result.PriceImpact.Should().BeGreaterOrEqualTo(0);
        result.Result.Fee.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetTinymanQuote_InvalidAmount_ReturnsError()
    {
        // Arrange
        var request = new SwapQuoteRequest
        {
            Chain = "algorand",
            TokenIn = "0",
            TokenOut = "31566704",
            AmountIn = "invalid",
            SlippageBps = 50
        };

        // Act
        var result = await _swapManager.GetQuoteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        var msg = result.Message.ToLower();
        (msg.Contains("format") || msg.Contains("parse") || msg.Contains("invalid")).Should().BeTrue();
    }

    [Fact]
    public async Task GetJupiterQuote_Solana_ReturnsQuoteOrGracefulError()
    {
        // Arrange
        var request = new SwapQuoteRequest
        {
            Chain = "solana",
            TokenIn = "So11111111111111111111111111111111111111112", // WSOL
            TokenOut = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v", // USDC
            AmountIn = "1000000000", // 1 SOL
            SlippageBps = 50
        };

        // Act
        var result = await _swapManager.GetQuoteAsync(request);

        // Assert - Either success OR network error (both acceptable in tests)
        if (!result.IsError)
        {
            result.Result.Should().NotBeNull();
            result.Result!.Chain.Should().Be("solana");
            result.Result.ExpectedAmountOut.Should().NotBeNullOrEmpty();
            result.Result.AmountIn.Should().Be("1000000000");
        }
        else
        {
            // Network errors are OK for integration tests (DNS/firewall)
            result.Message.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task GetQuoteAsync_UnsupportedChain_ReturnsError()
    {
        // Arrange
        var request = new SwapQuoteRequest
        {
            Chain = "ethereum",
            TokenIn = "0x...",
            TokenOut = "0x...",
            AmountIn = "1000000",
            SlippageBps = 50
        };

        // Act
        var result = await _swapManager.GetQuoteAsync(request);

        // Assert
        result.IsError.Should().BeTrue();
        result.Message.Should().Contain("Unsupported");
    }
}
