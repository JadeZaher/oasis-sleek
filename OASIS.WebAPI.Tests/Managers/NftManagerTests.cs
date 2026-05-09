using FluentAssertions;
using Moq;
using OASIS.WebAPI.Core;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Managers;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Tests.Managers;

public class NftManagerTests
{
    private readonly Mock<IOASISStorageProvider> _provider;
    private readonly NftManager _manager;

    public NftManagerTests()
    {
        _provider = new Mock<IOASISStorageProvider>();
        _provider.Setup(p => p.ProviderName).Returns("Test");
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var realContext = new ProviderContext(new[] { _provider.Object }, config);
        _manager = new NftManager(realContext);
    }

    private static INft CreateNftMock(Guid id, string name, string assetType = "NFT", Guid? avatarId = null, string? chainId = null) =>
        Moq.Mock.Of<INft>(n => n.Id == id && n.Name == name && n.AssetType == assetType && n.AvatarId == avatarId && n.ChainId == chainId && n.CreatedDate == DateTime.UtcNow);

    [Fact]
    public async Task GetAsync_NftFound_ReturnsSuccess()
    {
        var id = Guid.NewGuid();
        var nft = CreateNftMock(id, "NFT1");
        _provider.Setup(p => p.LoadHolonAsync(id, default))
            .ReturnsAsync(new OASISResult<IHolon> { Result = nft });

        var result = await _manager.GetAsync(id);

        result.IsError.Should().BeFalse();
        result.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAsync_NotAnNft_ReturnsError()
    {
        var id = Guid.NewGuid();
        var holon = CreateNftMock(id, "Regular", "Document");
        _provider.Setup(p => p.LoadHolonAsync(id, default))
            .ReturnsAsync(new OASISResult<IHolon> { Result = holon });

        var result = await _manager.GetAsync(id);

        result.IsError.Should().BeTrue();
        result.Message.Should().Contain("not an NFT");
    }

    [Fact]
    public async Task GetAsync_NotFound_ReturnsError()
    {
        _provider.Setup(p => p.LoadHolonAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(new OASISResult<IHolon> { IsError = true, Message = "Not found" });

        var result = await _manager.GetAsync(Guid.NewGuid());

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task QueryAsync_FiltersByOwnerAvatarId()
    {
        var avatarId = Guid.NewGuid();
        var nft1 = CreateNftMock(Guid.NewGuid(), "A", "NFT", avatarId);
        var nft2 = CreateNftMock(Guid.NewGuid(), "B", "NFT", Guid.NewGuid());
        _provider.Setup(p => p.LoadAllHolonsAsync(null, default))
            .ReturnsAsync(new OASISResult<IEnumerable<IHolon>> { Result = new List<IHolon> { nft1, nft2 } });

        var result = await _manager.QueryAsync(new NftQueryRequest { OwnerAvatarId = avatarId });

        result.Result.Should().ContainSingle();
    }

    [Fact]
    public async Task QueryAsync_FiltersByChainId()
    {
        var nft1 = CreateNftMock(Guid.NewGuid(), "A", "NFT", chainId: "solana");
        var nft2 = CreateNftMock(Guid.NewGuid(), "B", "NFT", chainId: "algorand");
        _provider.Setup(p => p.LoadAllHolonsAsync(null, default))
            .ReturnsAsync(new OASISResult<IEnumerable<IHolon>> { Result = new List<IHolon> { nft1, nft2 } });

        var result = await _manager.QueryAsync(new NftQueryRequest { ChainId = "solana" });

        result.Result.Should().ContainSingle();
    }

    [Fact]
    public async Task QueryAsync_FiltersNonNfts()
    {
        var nft = CreateNftMock(Guid.NewGuid(), "NFT", "NFT");
        var holon = CreateNftMock(Guid.NewGuid(), "Doc", "Document");
        _provider.Setup(p => p.LoadAllHolonsAsync(null, default))
            .ReturnsAsync(new OASISResult<IEnumerable<IHolon>> { Result = new List<IHolon> { nft, holon } });

        var result = await _manager.QueryAsync(new NftQueryRequest());

        result.Result.Should().ContainSingle();
    }

    [Fact]
    public async Task MintAsync_CreatesHolonWithNftAssetType()
    {
        var avatarId = Guid.NewGuid();
        var request = new NftMintRequest { Name = "MyNFT", Description = "Desc", ChainId = "solana", WalletId = Guid.NewGuid() };
        _provider.Setup(p => p.SaveHolonAsync(It.IsAny<IHolon>(), default))
            .ReturnsAsync(new OASISResult<IHolon> { Result = new Holon { Id = Guid.NewGuid(), AssetType = "NFT" } });
        _provider.Setup(p => p.SaveBlockchainOperationAsync(It.IsAny<IBlockchainOperation>(), default))
            .ReturnsAsync(new OASISResult<IBlockchainOperation> { Result = new BlockchainOperation { OperationType = "Mint" } });

        var result = await _manager.MintAsync(request, avatarId);

        result.IsError.Should().BeFalse();
        _provider.Verify(p => p.SaveHolonAsync(It.Is<IHolon>(h => h.AssetType == "NFT" && h.AvatarId == avatarId), default), Times.Once);
    }

    [Fact]
    public async Task TransferAsync_UpdatesOwnership()
    {
        var avatarId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var nftId = Guid.NewGuid();
        var nft = CreateNftMock(nftId, "NFT", "NFT", avatarId);
        _provider.Setup(p => p.LoadHolonAsync(nftId, default))
            .ReturnsAsync(new OASISResult<IHolon> { Result = nft });
        _provider.Setup(p => p.SaveHolonAsync(It.IsAny<IHolon>(), default))
            .ReturnsAsync(new OASISResult<IHolon> { Result = nft });
        _provider.Setup(p => p.SaveBlockchainOperationAsync(It.IsAny<IBlockchainOperation>(), default))
            .ReturnsAsync(new OASISResult<IBlockchainOperation> { Result = new BlockchainOperation() });

        var result = await _manager.TransferAsync(nftId, new NftTransferRequest { TargetAvatarId = targetId, WalletId = Guid.NewGuid() }, avatarId);

        result.IsError.Should().BeFalse();
        nft.AvatarId.Should().Be(targetId);
    }

    [Fact]
    public async Task TransferAsync_WrongOwner_ReturnsError()
    {
        var avatarId = Guid.NewGuid();
        var nft = CreateNftMock(Guid.NewGuid(), "NFT", "NFT", Guid.NewGuid());
        _provider.Setup(p => p.LoadHolonAsync(nft.Id, default))
            .ReturnsAsync(new OASISResult<IHolon> { Result = nft });

        var result = await _manager.TransferAsync(nft.Id, new NftTransferRequest(), avatarId);

        result.IsError.Should().BeTrue();
        result.Message.Should().Contain("do not own");
    }

    [Fact]
    public async Task BurnAsync_DeactivatesHolon()
    {
        var avatarId = Guid.NewGuid();
        var nft = CreateNftMock(Guid.NewGuid(), "NFT", "NFT", avatarId);
        nft.IsActive = true;
        _provider.Setup(p => p.LoadHolonAsync(nft.Id, default))
            .ReturnsAsync(new OASISResult<IHolon> { Result = nft });
        _provider.Setup(p => p.SaveHolonAsync(It.IsAny<IHolon>(), default))
            .ReturnsAsync(new OASISResult<IHolon> { Result = nft });
        _provider.Setup(p => p.SaveBlockchainOperationAsync(It.IsAny<IBlockchainOperation>(), default))
            .ReturnsAsync(new OASISResult<IBlockchainOperation> { Result = new BlockchainOperation() });

        var result = await _manager.BurnAsync(nft.Id, Guid.NewGuid(), avatarId);

        result.IsError.Should().BeFalse();
        nft.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetMetadataAsync_ReturnsErc721Shape()
    {
        var nft = Moq.Mock.Of<INft>(n => n.Id == Guid.NewGuid() && n.Name == "MyNFT" && n.AssetType == "NFT" && n.CreatedDate == DateTime.UtcNow);
        var mock = Moq.Mock.Get(nft);
        mock.SetupGet(n => n.Description).Returns("A cool NFT");
        mock.SetupGet(n => n.Metadata).Returns(new Dictionary<string, string>
        {
            ["image"] = "https://example.com/img.png",
            ["external_url"] = "https://example.com"
        });
        _provider.Setup(p => p.LoadHolonAsync(nft.Id, default))
            .ReturnsAsync(new OASISResult<IHolon> { Result = nft });

        var result = await _manager.GetMetadataAsync(nft.Id);

        result.IsError.Should().BeFalse();
        result.Result!.Name.Should().Be("MyNFT");
        result.Result!.Image.Should().Be("https://example.com/img.png");
        result.Result!.ExternalUrl.Should().Be("https://example.com");
    }
}
