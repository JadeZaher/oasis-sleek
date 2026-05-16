using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OASIS.WebAPI.Core;
using OASIS.WebAPI.Core.Blockchain.Wormhole;
using OASIS.WebAPI.Data;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Models.Responses;
using OASIS.WebAPI.Services;

namespace OASIS.WebAPI.Tests.Services;

public class CrossChainBridgeServiceTests
{
    private readonly Mock<IBlockchainProviderFactory> _factoryMock;
    private readonly Mock<IWormholeAdapter> _wormholeMock;
    private readonly Mock<IBlockchainProvider> _providerMock;
    private readonly WormholeConfig _config;
    private readonly CrossChainBridgeService _service;

    public CrossChainBridgeServiceTests()
    {
        _factoryMock = new Mock<IBlockchainProviderFactory>();
        _wormholeMock = new Mock<IWormholeAdapter>();
        _providerMock = new Mock<IBlockchainProvider>();
        _config = new WormholeConfig { DefaultMode = BridgeMode.Wormhole };

        _providerMock.Setup(p => p.SupportsBridging).Returns(true);
        _providerMock.Setup(p => p.ChainType).Returns("Solana");

        _factoryMock.Setup(f => f.GetProvider(It.IsAny<string>(), It.IsAny<ChainNetwork>()))
            .Returns(_providerMock.Object);

        // Tests use an isolated in-memory store; the main app stays on the
        // persisted Npgsql/PostgreSQL provider (see Program.cs).
        var dbOptions = new DbContextOptionsBuilder<OASISDbContext>()
            .UseInMemoryDatabase($"BridgeTests_{Guid.NewGuid()}")
            .Options;
        var db = new OASISDbContext(dbOptions);

        _service = new CrossChainBridgeService(
            _factoryMock.Object,
            _wormholeMock.Object,
            Options.Create(_config),
            db,
            Mock.Of<ILogger<CrossChainBridgeService>>());
    }

    // ─── Initiation ───

    [Fact]
    public async Task InitiateBridge_EmptySourceChain_ReturnsError()
    {
        var result = await _service.InitiateBridgeAsync("", "Algorand", "token", "addr", Guid.NewGuid());
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task InitiateBridge_TrustedMode_CompletesImmediately()
    {
        _providerMock.Setup(p => p.LockForBridgeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<string> { IsError = false, Result = "lock_tx" });

        _providerMock.Setup(p => p.MintWrappedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<string> { IsError = false, Result = "mint_tx" });

        var result = await _service.InitiateBridgeAsync(
            "Solana", "Algorand", "token1", "recipient", Guid.NewGuid(), 1, BridgeMode.Trusted);

        result.IsError.Should().BeFalse();
        result.Result!.Status.Should().Be(BridgeStatus.Completed);
        result.Result.Mode.Should().Be(BridgeMode.Trusted);
        result.Result.LockTxHash.Should().Be("lock_tx");
        result.Result.MintTxHash.Should().Be("mint_tx");
    }

    [Fact]
    public async Task InitiateBridge_WormholeMode_ReturnsAwaitingVAA()
    {
        _wormholeMock.Setup(w => w.IsRouteSupported("Solana", "Algorand")).Returns(true);
        _wormholeMock.Setup(w => w.InitiateTransferAsync(
            "Solana", "Algorand", "token1", It.IsAny<string>(), "recipient",
            1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<WormholeTransferInitiation>
            {
                IsError = false,
                Result = new WormholeTransferInitiation
                {
                    TxHash = "wh_lock_tx",
                    EmitterChainId = 1,
                    EmitterAddress = "emitter",
                    Sequence = 42
                }
            });

        var result = await _service.InitiateBridgeAsync(
            "Solana", "Algorand", "token1", "recipient", Guid.NewGuid(), 1, BridgeMode.Wormhole);

        result.IsError.Should().BeFalse();
        result.Result!.Status.Should().Be(BridgeStatus.AwaitingVAA);
        result.Result.Mode.Should().Be(BridgeMode.Wormhole);
        result.Result.WormholeSequence.Should().Be(42);
        result.Result.WormholeEmitterChainId.Should().Be(1);
        result.Result.Id.Should().StartWith("wh_bridge_");
    }

    [Fact]
    public async Task InitiateBridge_WormholeUnsupported_FallsBackToTrusted()
    {
        _wormholeMock.Setup(w => w.IsRouteSupported("Solana", "Algorand")).Returns(false);

        _providerMock.Setup(p => p.LockForBridgeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<string> { IsError = false, Result = "lock_tx" });

        _providerMock.Setup(p => p.MintWrappedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<string> { IsError = false, Result = "mint_tx" });

        var result = await _service.InitiateBridgeAsync(
            "Solana", "Algorand", "token1", "recipient", Guid.NewGuid(), 1, BridgeMode.Wormhole);

        result.IsError.Should().BeFalse();
        result.Result!.Mode.Should().Be(BridgeMode.Trusted);
        result.Result.Status.Should().Be(BridgeStatus.Completed);
    }

    // ─── Full Wormhole lifecycle ───

    [Fact]
    public async Task WormholeBridge_FullLifecycle_InitiateFetchRedeem()
    {
        var avatarId = Guid.NewGuid();

        // Step 1: Initiate
        _wormholeMock.Setup(w => w.IsRouteSupported("Solana", "Algorand")).Returns(true);
        _wormholeMock.Setup(w => w.InitiateTransferAsync(
            "Solana", "Algorand", "token1", It.IsAny<string>(), "recipient",
            1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<WormholeTransferInitiation>
            {
                IsError = false,
                Result = new WormholeTransferInitiation
                {
                    TxHash = "wh_tx", EmitterChainId = 1,
                    EmitterAddress = "emitter", Sequence = 100
                }
            });

        var initResult = await _service.InitiateBridgeAsync(
            "Solana", "Algorand", "token1", "recipient", avatarId, 1, BridgeMode.Wormhole);

        initResult.IsError.Should().BeFalse();
        var bridgeId = initResult.Result!.Id;

        // Step 2: Fetch VAA
        _wormholeMock.Setup(w => w.FetchVAAAsync(1, "emitter", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<WormholeVAA>
            {
                IsError = false,
                Result = new WormholeVAA
                {
                    VaaBytes = "AQIDBA==",
                    SignatureCount = 13,
                    Sequence = 100,
                    EmitterChainId = 1,
                    EmitterAddress = "emitter",
                    Digest = "0xabc123"
                }
            });

        var fetchResult = await _service.FetchVAAAsync(bridgeId);
        fetchResult.IsError.Should().BeFalse();
        fetchResult.Result!.Status.Should().Be(BridgeStatus.VAAReady);
        fetchResult.Result.VaaBytes.Should().Be("AQIDBA==");
        fetchResult.Result.VaaSignatureCount.Should().Be(13);

        // Step 3: Redeem
        _wormholeMock.Setup(w => w.RedeemTransferAsync(
            "Algorand", It.IsAny<WormholeVAA>(), "recipient", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<WormholeRedemptionResult>
            {
                IsError = false,
                Result = new WormholeRedemptionResult
                {
                    TxHash = "redeem_tx_789",
                    Success = true
                }
            });

        var redeemResult = await _service.RedeemWithVAAAsync(bridgeId);
        redeemResult.IsError.Should().BeFalse();
        redeemResult.Result!.Status.Should().Be(BridgeStatus.Completed);
        redeemResult.Result.RedemptionTxHash.Should().Be("redeem_tx_789");
        redeemResult.Result.CompletedAt.Should().NotBeNull();
    }

    // ─── Error paths ───

    [Fact]
    public async Task FetchVAA_TrustedBridge_ReturnsError()
    {
        _providerMock.Setup(p => p.LockForBridgeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<string> { IsError = false, Result = "tx" });
        _providerMock.Setup(p => p.MintWrappedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<string> { IsError = false, Result = "tx" });

        var initResult = await _service.InitiateBridgeAsync(
            "Solana", "Algorand", "token1", "r", Guid.NewGuid(), 1, BridgeMode.Trusted);
        var bridgeId = initResult.Result!.Id;

        var fetchResult = await _service.FetchVAAAsync(bridgeId);
        fetchResult.IsError.Should().BeTrue();
        fetchResult.Message.Should().Contain("only available for Wormhole");
    }

    [Fact]
    public async Task RedeemWithVAA_NotVAAReady_ReturnsError()
    {
        _wormholeMock.Setup(w => w.IsRouteSupported("Solana", "Algorand")).Returns(true);
        _wormholeMock.Setup(w => w.InitiateTransferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<WormholeTransferInitiation>
            {
                IsError = false,
                Result = new WormholeTransferInitiation
                {
                    TxHash = "tx", EmitterChainId = 1,
                    EmitterAddress = "e", Sequence = 1
                }
            });

        var initResult = await _service.InitiateBridgeAsync(
            "Solana", "Algorand", "token1", "r", Guid.NewGuid(), 1, BridgeMode.Wormhole);
        var bridgeId = initResult.Result!.Id;

        // Try to redeem without fetching VAA first
        var redeemResult = await _service.RedeemWithVAAAsync(bridgeId);
        redeemResult.IsError.Should().BeTrue();
        redeemResult.Message.Should().Contain("expected VAAReady");
    }

    [Fact]
    public async Task FetchVAA_NotFound_ReturnsError()
    {
        var result = await _service.FetchVAAAsync("nonexistent");
        result.IsError.Should().BeTrue();
        result.Message.Should().Contain("not found");
    }

    // ─── Routes ───

    [Fact]
    public async Task GetSupportedRoutes_IncludesWormholeInfo()
    {
        _factoryMock.Setup(f => f.GetAllEnabledProviders())
            .Returns(new[]
            {
                CreateMockProvider("Solana", true),
                CreateMockProvider("Algorand", true)
            });

        _wormholeMock.Setup(w => w.IsRouteSupported("Solana", "Algorand")).Returns(true);
        _wormholeMock.Setup(w => w.IsRouteSupported("Algorand", "Solana")).Returns(true);
        _wormholeMock.Setup(w => w.GetWormholeChainId("Solana")).Returns(1);
        _wormholeMock.Setup(w => w.GetWormholeChainId("Algorand")).Returns(8);

        var result = await _service.GetSupportedRoutesAsync();

        result.IsError.Should().BeFalse();
        var routes = result.Result!.ToList();
        routes.Should().HaveCount(2);

        var solToAlgo = routes.First(r => r.SourceChain == "Solana" && r.TargetChain == "Algorand");
        solToAlgo.WormholeSupported.Should().BeTrue();
        solToAlgo.AvailableModes.Should().Contain(BridgeMode.Wormhole);
        solToAlgo.WormholeSourceChainId.Should().Be(1);
        solToAlgo.WormholeTargetChainId.Should().Be(8);
    }

    // ─── History ───

    [Fact]
    public async Task GetBridgeHistory_ReturnsAvatarBridges()
    {
        var avatarId = Guid.NewGuid();

        _providerMock.Setup(p => p.LockForBridgeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<string> { IsError = false, Result = "tx" });
        _providerMock.Setup(p => p.MintWrappedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<string> { IsError = false, Result = "tx" });

        await _service.InitiateBridgeAsync("Solana", "Algorand", "t1", "r", avatarId, 1, BridgeMode.Trusted);
        await _service.InitiateBridgeAsync("Solana", "Algorand", "t2", "r", avatarId, 2, BridgeMode.Trusted);

        var history = await _service.GetBridgeHistoryAsync(avatarId);
        history.IsError.Should().BeFalse();
        history.Result!.Count().Should().Be(2);
    }

    private static IBlockchainProvider CreateMockProvider(string chainType, bool supportsBridging)
    {
        var mock = new Mock<IBlockchainProvider>();
        mock.Setup(p => p.ChainType).Returns(chainType);
        mock.Setup(p => p.SupportsBridging).Returns(supportsBridging);
        return mock.Object;
    }
}
