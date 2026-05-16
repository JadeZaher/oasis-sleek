using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using OASIS.WebAPI.Core;
using OASIS.WebAPI.Core.Blockchain.Wormhole;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Models.Responses;
using OASIS.WebAPI.Services;

namespace OASIS.WebAPI.Tests.Services;

public class WormholeAdapterTests
{
    private readonly Mock<IBlockchainProviderFactory> _factoryMock;
    private readonly Mock<IBlockchainProvider> _providerMock;
    private readonly WormholeConfig _config;
    private readonly Mock<ILogger<WormholeAdapter>> _loggerMock;

    public WormholeAdapterTests()
    {
        _factoryMock = new Mock<IBlockchainProviderFactory>();
        _providerMock = new Mock<IBlockchainProvider>();
        _loggerMock = new Mock<ILogger<WormholeAdapter>>();
        _config = new WormholeConfig
        {
            GuardianRpcUrl = "https://test-guardian.example.com",
            VaaTimeoutSeconds = 5,
            VaaPollIntervalMs = 100,
            MinGuardianSignatures = 13,
            ChainMappings = new Dictionary<string, WormholeChainMapping>
            {
                ["Solana"] = new() { WormholeChainId = 1, CoreBridgeAddress = "worm2ZoG..." },
                ["Algorand"] = new() { WormholeChainId = 8, CoreBridgeAddress = "842125965" }
            }
        };
    }

    private WormholeAdapter CreateAdapter(HttpClient? httpClient = null)
    {
        var client = httpClient ?? new HttpClient(new FakeHandler(HttpStatusCode.OK, "{}"))
        {
            BaseAddress = new Uri(_config.GuardianRpcUrl)
        };
        return new WormholeAdapter(
            client,
            _factoryMock.Object,
            Options.Create(_config),
            _loggerMock.Object);
    }

    [Fact]
    public void IsRouteSupported_SolanaToAlgorand_ReturnsTrue()
    {
        var adapter = CreateAdapter();
        adapter.IsRouteSupported("Solana", "Algorand").Should().BeTrue();
    }

    [Fact]
    public void IsRouteSupported_SameChain_ReturnsFalse()
    {
        var adapter = CreateAdapter();
        adapter.IsRouteSupported("Solana", "Solana").Should().BeFalse();
    }

    [Fact]
    public void IsRouteSupported_UnknownChain_ReturnsFalse()
    {
        var adapter = CreateAdapter();
        adapter.IsRouteSupported("Ethereum", "Solana").Should().BeFalse();
    }

    [Fact]
    public void GetWormholeChainId_Solana_Returns1()
    {
        var adapter = CreateAdapter();
        adapter.GetWormholeChainId("Solana").Should().Be(1);
    }

    [Fact]
    public void GetWormholeChainId_Algorand_Returns8()
    {
        var adapter = CreateAdapter();
        adapter.GetWormholeChainId("Algorand").Should().Be(8);
    }

    [Fact]
    public void GetWormholeChainId_Unknown_ReturnsNull()
    {
        var adapter = CreateAdapter();
        adapter.GetWormholeChainId("Bitcoin").Should().BeNull();
    }

    [Fact]
    public async Task InitiateTransferAsync_UnsupportedRoute_ReturnsError()
    {
        var adapter = CreateAdapter();

        var result = await adapter.InitiateTransferAsync(
            "Ethereum", "Solana", "token1", "sender", "recipient", 1);

        result.IsError.Should().BeTrue();
        result.Message.Should().Contain("not supported");
    }

    [Fact]
    public async Task InitiateTransferAsync_LockFails_ReturnsError()
    {
        _providerMock.Setup(p => p.LockForBridgeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<string> { IsError = true, Message = "Lock failed" });

        _factoryMock.Setup(f => f.GetProvider("Solana", ChainNetwork.Devnet))
            .Returns(_providerMock.Object);

        var adapter = CreateAdapter();
        var result = await adapter.InitiateTransferAsync(
            "Solana", "Algorand", "token1", "sender", "recipient", 1);

        result.IsError.Should().BeTrue();
        result.Message.Should().Contain("lock failed");
    }

    [Fact]
    public async Task InitiateTransferAsync_Success_ReturnsInitiation()
    {
        _providerMock.Setup(p => p.LockForBridgeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<string> { IsError = false, Result = "tx_hash_123" });

        _factoryMock.Setup(f => f.GetProvider("Solana", ChainNetwork.Devnet))
            .Returns(_providerMock.Object);

        var adapter = CreateAdapter();
        var result = await adapter.InitiateTransferAsync(
            "Solana", "Algorand", "token1", "sender", "recipient", 5);

        result.IsError.Should().BeFalse();
        result.Result.Should().NotBeNull();
        result.Result!.TxHash.Should().Be("tx_hash_123");
        result.Result.EmitterChainId.Should().Be(1); // Solana
    }

    [Fact]
    public async Task VerifyVAAAsync_EmptyBytes_ReturnsError()
    {
        var adapter = CreateAdapter();
        var vaa = new WormholeVAA { VaaBytes = "" };

        var result = await adapter.VerifyVAAAsync(vaa);

        result.IsError.Should().BeTrue();
        result.Message.Should().Contain("empty");
    }

    [Fact]
    public async Task VerifyVAAAsync_InsufficientSignatures_ReturnsError()
    {
        var adapter = CreateAdapter();
        var vaa = new WormholeVAA { VaaBytes = "AQID", SignatureCount = 5, Version = 1 };

        var result = await adapter.VerifyVAAAsync(vaa);

        result.IsError.Should().BeTrue();
        result.Message.Should().Contain("Insufficient");
    }

    [Fact]
    public async Task VerifyVAAAsync_ValidVAA_ReturnsTrue()
    {
        var adapter = CreateAdapter();
        var vaa = new WormholeVAA { VaaBytes = "AQID", SignatureCount = 13, Version = 1 };

        var result = await adapter.VerifyVAAAsync(vaa);

        result.IsError.Should().BeFalse();
        result.Result.Should().BeTrue();
    }

    [Fact]
    public async Task RedeemTransferAsync_UnknownTarget_ReturnsError()
    {
        var adapter = CreateAdapter();
        var vaa = new WormholeVAA { VaaBytes = "AQID", SignatureCount = 13, Version = 1 };

        var result = await adapter.RedeemTransferAsync("Ethereum", vaa, "recipient");

        result.IsError.Should().BeTrue();
        result.Message.Should().Contain("Unknown target");
    }

    [Fact]
    public async Task RedeemTransferAsync_Success_ReturnsRedemption()
    {
        _providerMock.Setup(p => p.MintWrappedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<string> { IsError = false, Result = "mint_tx_456" });

        _factoryMock.Setup(f => f.GetProvider("Algorand", ChainNetwork.Devnet))
            .Returns(_providerMock.Object);

        var adapter = CreateAdapter();
        var vaa = new WormholeVAA
        {
            VaaBytes = "AQID",
            SignatureCount = 13,
            Version = 1,
            EmitterChainId = 1,
            EmitterAddress = "test",
            Sequence = 42
        };

        var result = await adapter.RedeemTransferAsync("Algorand", vaa, "recipient");

        result.IsError.Should().BeFalse();
        result.Result!.Success.Should().BeTrue();
        result.Result.TxHash.Should().Be("mint_tx_456");
    }

    [Fact]
    public async Task FetchVAAAsync_GuardianReturnsVAA_Success()
    {
        // Build a minimal valid VAA: version=1, guardianSetIndex=0, sigCount=13, then padding
        var vaaBody = new byte[100];
        vaaBody[0] = 1;   // version
        vaaBody[5] = 13;  // 13 signatures
        var vaaBase64 = Convert.ToBase64String(vaaBody);

        var responseJson = JsonSerializer.Serialize(new
        {
            data = new { vaaBytes = vaaBase64 }
        });

        var handler = new FakeHandler(HttpStatusCode.OK, responseJson);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(_config.GuardianRpcUrl) };

        var adapter = CreateAdapter(httpClient);
        var result = await adapter.FetchVAAAsync(1, "emitter_addr", 42);

        result.IsError.Should().BeFalse();
        result.Result!.Sequence.Should().Be(42);
        result.Result.EmitterChainId.Should().Be(1);
        result.Result.SignatureCount.Should().Be(13);
    }

    [Fact]
    public async Task FetchVAAAsync_Timeout_ReturnsError()
    {
        _config.VaaTimeoutSeconds = 1;
        _config.VaaPollIntervalMs = 200;

        var handler = new FakeHandler(HttpStatusCode.NotFound, "");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(_config.GuardianRpcUrl) };

        var adapter = CreateAdapter(httpClient);
        var result = await adapter.FetchVAAAsync(1, "emitter", 99);

        result.IsError.Should().BeTrue();
        result.Message.Should().Contain("timed out");
    }

    // ─── Test HTTP handler ───

    private class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public FakeHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
