using Moq;
using NuDepot.Core.Models;
using NuDepot.Core.Search;
using NuDepot.Gallery.Pages;
using Xunit;

namespace NuDepot.Gallery.Tests;

public class IndexModelFacts
{
    private Mock<ISearchClient> Search { get; }
    private IndexModel Target { get; }

    private SearchResponse Response { get; } = new() { Data = [] };
    private CancellationToken Cancellation { get; } = CancellationToken.None;

    public IndexModelFacts()
    {
        Search = new Mock<ISearchClient>();
        Search
            .Setup(s => s.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                Cancellation))
            .ReturnsAsync(Response);

        Target = new IndexModel(Search.Object, FeedConnectionFacts.CreateRemoteFeed());
    }

    [Fact]
    public async Task DefaultSearch()
    {
        await Target.OnGetAsync(Cancellation);

        Search.Verify(s => s.SearchAsync(
            /*query: */ null,
            /*skip: */ 0,
            /*take: */ 20,
            /*includePrerelease: */ true,
            /*includeSemVer2: */ true,
            /*packageType: */ null,
            /*framework: */ null,
            Cancellation));
    }

    [Fact]
    public async Task AcceptsParameters()
    {
        Target.Prerelease = false;
        Target.PageIndex = 5;
        Target.PackageType = "foo";
        Target.Framework = "bar";
        Target.Query = "Hello world";

        await Target.OnGetAsync(Cancellation);

        Search.Verify(s => s.SearchAsync(
            /*query: */ "Hello world",
            /*skip: */ 80,
            /*take: */ 20,
            /*includePrerelease: */ false,
            /*includeSemVer2: */ true,
            /*packageType: */ "foo",
            /*framework: */ "bar",
            Cancellation));
    }
}
