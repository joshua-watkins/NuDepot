using NuDepot.Core.Models;
using NuDepot.Core.Search;
using NuDepot.Core.Tests.Support;
using Xunit;

namespace NuDepot.Core.Tests;

public class RawSearchClientTests(ProtocolFixture fixture) : IClassFixture<ProtocolFixture>
{
    private RawSearchClient Target { get; } = fixture.SearchClient;

    [Fact]
    public async Task GetDefaultSearchResults()
    {
        SearchResponse response = await Target.SearchAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal(1, response.TotalHits);

        SearchResult result = Assert.Single(response.Data);
        Assert.Equal("Test.Package", result.PackageId);
        Assert.Equal("Package Authors", Assert.Single(result.Authors!));
        Assert.Equal(TestData.RegistrationIndexInlinedItemsUrl, result.RegistrationIndexUrl);

        SearchResultPackageType packageType = Assert.Single(result.PackageTypes!);
        Assert.Equal("Dependency", packageType.Name);
    }

    [Fact]
    public async Task AddsParameters()
    {
        await Task.Yield();

        // TODO: Assert request URL query parameters.
        // var response = await _target.SearchAsync(
        //     "query",
        //     skip: 2,
        //     take: 5,
        //     includePrerelease: false,
        //     includeSemVer2: false);
    }
}
