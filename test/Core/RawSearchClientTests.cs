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
        RecordingHttpMessageHandler handler = new(TestData.DefaultSearch);
        RawSearchClient client = new(new HttpClient(handler), TestData.SearchUrl);

        await client.SearchAsync("query", skip: 2, take: 5, includePrerelease: false, includeSemVer2: false, packageType: "Dependency", framework: "net10.0", TestContext.Current.CancellationToken);

        Assert.Equal($"{TestData.SearchUrl}?skip=2&take=5&packageType=Dependency&framework=net10.0&q=query", Assert.Single(handler.RequestedUrls));
    }
}
