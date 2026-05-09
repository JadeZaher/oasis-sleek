using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OASIS.WebAPI.Controllers;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Tests.Controllers;

public class SearchControllerTests
{
    private readonly Mock<ISearchManager> _searchManager;
    private readonly SearchController _controller;

    public SearchControllerTests()
    {
        _searchManager = new Mock<ISearchManager>();
        _controller = new SearchController(_searchManager.Object);
    }

    [Fact]
    public async Task Search_ReturnsOk()
    {
        _searchManager.Setup(m => m.SearchAsync(It.IsAny<SearchRequest>(), null))
            .ReturnsAsync(new OASISResult<SearchResult> { Result = new SearchResult() });

        var result = await _controller.Search(new SearchRequest(), null);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Search_Error_ReturnsBadRequest()
    {
        _searchManager.Setup(m => m.SearchAsync(It.IsAny<SearchRequest>(), null))
            .ReturnsAsync(new OASISResult<SearchResult> { IsError = true, Message = "Error" });

        var result = await _controller.Search(new SearchRequest(), null);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetFacets_ReturnsOk()
    {
        _searchManager.Setup(m => m.GetFacetsAsync(null))
            .ReturnsAsync(new OASISResult<List<SearchFacet>> { Result = new List<SearchFacet>() });

        var result = await _controller.GetFacets(null);

        result.Result.Should().BeOfType<OkObjectResult>();
    }
}
