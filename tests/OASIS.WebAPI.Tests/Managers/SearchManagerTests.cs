using FluentAssertions;
using Moq;
using OASIS.WebAPI.Core;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Managers;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Tests.Managers;

public class SearchManagerTests
{
    private readonly Mock<IOASISStorageProvider> _provider;
    private readonly SearchManager _manager;

    public SearchManagerTests()
    {
        _provider = new Mock<IOASISStorageProvider>();
        _provider.Setup(p => p.ProviderName).Returns("Test");
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var realContext = new ProviderContext(new[] { _provider.Object }, config);
        _manager = new SearchManager(realContext);
    }

    [Fact]
    public async Task SearchAsync_ReturnsHitsFromAvatars()
    {
        var avatar = new Avatar { Id = Guid.NewGuid(), Username = "neo", Email = "neo@matrix.com", CreatedDate = DateTime.UtcNow };
        _provider.Setup(p => p.LoadAllAvatarsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<IAvatar>> { Result = new List<IAvatar> { avatar } });
        _provider.Setup(p => p.LoadAllHolonsAsync(null, default))
            .ReturnsAsync(new OASISResult<IEnumerable<IHolon>> { Result = new List<IHolon>() });
        _provider.Setup(p => p.LoadAllWalletsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<IWallet>> { Result = new List<IWallet>() });
        _provider.Setup(p => p.LoadAllSTARODKsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<ISTARODK>> { Result = new List<ISTARODK>() });

        var result = await _manager.SearchAsync(new SearchRequest { Query = "neo", EntityTypes = SearchableEntityType.Avatar });

        result.IsError.Should().BeFalse();
        result.Result!.Hits.Should().ContainSingle();
        result.Result!.Hits[0].EntityType.Should().Be(SearchableEntityType.Avatar);
        result.Result!.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_ReturnsHitsFromHolons()
    {
        var holon = new Holon { Id = Guid.NewGuid(), Name = "WorldHolon", Description = "A world", AssetType = "World", CreatedDate = DateTime.UtcNow };
        _provider.Setup(p => p.LoadAllAvatarsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<IAvatar>> { Result = new List<IAvatar>() });
        _provider.Setup(p => p.LoadAllHolonsAsync(null, default))
            .ReturnsAsync(new OASISResult<IEnumerable<IHolon>> { Result = new List<IHolon> { holon } });
        _provider.Setup(p => p.LoadAllWalletsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<IWallet>> { Result = new List<IWallet>() });
        _provider.Setup(p => p.LoadAllSTARODKsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<ISTARODK>> { Result = new List<ISTARODK>() });

        var result = await _manager.SearchAsync(new SearchRequest { Query = "world", EntityTypes = SearchableEntityType.Holon });

        result.IsError.Should().BeFalse();
        result.Result!.Hits.Should().ContainSingle();
        result.Result!.Hits[0].EntityType.Should().Be(SearchableEntityType.Holon);
    }

    [Fact]
    public async Task SearchAsync_CaseInsensitiveQuery()
    {
        var avatar = new Avatar { Id = Guid.NewGuid(), Username = "Neo", Email = "neo@matrix.com", CreatedDate = DateTime.UtcNow };
        _provider.Setup(p => p.LoadAllAvatarsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<IAvatar>> { Result = new List<IAvatar> { avatar } });
        _provider.Setup(p => p.LoadAllHolonsAsync(null, default))
            .ReturnsAsync(new OASISResult<IEnumerable<IHolon>> { Result = new List<IHolon>() });
        _provider.Setup(p => p.LoadAllWalletsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<IWallet>> { Result = new List<IWallet>() });
        _provider.Setup(p => p.LoadAllSTARODKsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<ISTARODK>> { Result = new List<ISTARODK>() });

        var result = await _manager.SearchAsync(new SearchRequest { Query = "NEO", EntityTypes = SearchableEntityType.Avatar });

        result.Result!.Hits.Should().ContainSingle();
    }

    [Fact]
    public async Task SearchAsync_PaginationWorks()
    {
        var avatars = Enumerable.Range(0, 10).Select(i => new Avatar
        {
            Id = Guid.NewGuid(),
            Username = $"user{i}",
            Email = $"user{i}@test.com",
            CreatedDate = DateTime.UtcNow
        }).Cast<IAvatar>().ToList();

        _provider.Setup(p => p.LoadAllAvatarsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<IAvatar>> { Result = avatars });
        _provider.Setup(p => p.LoadAllHolonsAsync(null, default))
            .ReturnsAsync(new OASISResult<IEnumerable<IHolon>> { Result = new List<IHolon>() });
        _provider.Setup(p => p.LoadAllWalletsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<IWallet>> { Result = new List<IWallet>() });
        _provider.Setup(p => p.LoadAllSTARODKsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<ISTARODK>> { Result = new List<ISTARODK>() });

        var result = await _manager.SearchAsync(new SearchRequest { Query = "user", EntityTypes = SearchableEntityType.Avatar, Page = 1, PageSize = 3 });

        result.Result!.TotalCount.Should().Be(10);
        result.Result!.TotalPages.Should().Be(4);
        result.Result!.Hits.Should().HaveCount(3);
        result.Result!.PageSize.Should().Be(3);
    }

    [Fact]
    public async Task SearchAsync_EmptyQueryReturnsAllMatching()
    {
        var avatars = new List<IAvatar>
        {
            new Avatar { Id = Guid.NewGuid(), Username = "alice", Email = "a@x.com", CreatedDate = DateTime.UtcNow },
            new Avatar { Id = Guid.NewGuid(), Username = "bob", Email = "b@x.com", CreatedDate = DateTime.UtcNow }
        };

        _provider.Setup(p => p.LoadAllAvatarsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<IAvatar>> { Result = avatars });
        _provider.Setup(p => p.LoadAllHolonsAsync(null, default))
            .ReturnsAsync(new OASISResult<IEnumerable<IHolon>> { Result = new List<IHolon>() });
        _provider.Setup(p => p.LoadAllWalletsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<IWallet>> { Result = new List<IWallet>() });
        _provider.Setup(p => p.LoadAllSTARODKsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<ISTARODK>> { Result = new List<ISTARODK>() });

        var result = await _manager.SearchAsync(new SearchRequest { Query = "", EntityTypes = SearchableEntityType.Avatar });

        result.Result!.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_FiltersByAssetType()
    {
        var nftHolon = new Holon { Id = Guid.NewGuid(), Name = "NFT1", AssetType = "NFT", CreatedDate = DateTime.UtcNow };
        var docHolon = new Holon { Id = Guid.NewGuid(), Name = "Doc1", AssetType = "Document", CreatedDate = DateTime.UtcNow };
        _provider.Setup(p => p.LoadAllAvatarsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<IAvatar>> { Result = new List<IAvatar>() });
        _provider.Setup(p => p.LoadAllHolonsAsync(null, default))
            .ReturnsAsync(new OASISResult<IEnumerable<IHolon>> { Result = new List<IHolon> { nftHolon, docHolon } });
        _provider.Setup(p => p.LoadAllWalletsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<IWallet>> { Result = new List<IWallet>() });
        _provider.Setup(p => p.LoadAllSTARODKsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<ISTARODK>> { Result = new List<ISTARODK>() });

        var result = await _manager.SearchAsync(new SearchRequest { Query = "", EntityTypes = SearchableEntityType.Holon, AssetType = "NFT" });

        result.Result!.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetFacetsAsync_ReturnsAllEntityCounts()
    {
        _provider.Setup(p => p.LoadAllAvatarsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<IAvatar>> { Result = new List<IAvatar> { new Avatar() } });
        _provider.Setup(p => p.LoadAllHolonsAsync(null, default))
            .ReturnsAsync(new OASISResult<IEnumerable<IHolon>> { Result = new List<IHolon> { new Holon(), new Holon() } });
        _provider.Setup(p => p.LoadAllWalletsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<IWallet>> { Result = new List<IWallet> { new Wallet() } });
        _provider.Setup(p => p.LoadAllSTARODKsAsync(default))
            .ReturnsAsync(new OASISResult<IEnumerable<ISTARODK>> { Result = new List<ISTARODK>() });

        var result = await _manager.GetFacetsAsync();

        result.Result.Should().HaveCount(4);
        result.Result!.First(f => f.EntityType == SearchableEntityType.Avatar).Count.Should().Be(1);
        result.Result!.First(f => f.EntityType == SearchableEntityType.Holon).Count.Should().Be(2);
    }
}
