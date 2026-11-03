using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NuGet.Core.Models;
using NuGet.Core.Search;
using NuGet.Gallery.Pages;
using Xunit;

namespace NuGet.Gallery.Tests;

public class IndexModelFacts
{
    private readonly Mock<ISearchClient> _search;
    private readonly IndexModel _target;

    private readonly SearchResponse _response = new SearchResponse { Data = new List<SearchResult>() };
    private readonly CancellationToken _cancellation = CancellationToken.None;

    public IndexModelFacts()
    {
        _search = new Mock<ISearchClient>();
        _search
            .Setup(s => s.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                _cancellation))
            .ReturnsAsync(_response);

        _target = new IndexModel(_search.Object, FeedConnectionFacts.CreateRemoteFeed());
    }

    [Fact]
    public async Task DefaultSearch()
    {
        await _target.OnGetAsync(_cancellation);

        _search.Verify(s => s.SearchAsync(
            /*query: */ null,
            /*skip: */ 0,
            /*take: */ 20,
            /*includePrerelease: */ true,
            /*includeSemVer2: */ true,
            /*packageType: */ null,
            /*framework: */ null,
            _cancellation));
    }

    [Fact]
    public async Task AcceptsParameters()
    {
        _target.Prerelease = false;
        _target.PageIndex = 5;
        _target.PackageType = "foo";
        _target.Framework = "bar";
        _target.Query = "Hello world";

        await _target.OnGetAsync(_cancellation);

        _search.Verify(s => s.SearchAsync(
            /*query: */ "Hello world",
            /*skip: */ 80,
            /*take: */ 20,
            /*includePrerelease: */ false,
            /*includeSemVer2: */ true,
            /*packageType: */ "foo",
            /*framework: */ "bar",
            _cancellation));
    }
}
