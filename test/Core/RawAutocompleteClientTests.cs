using NuDepot.Core.Models;
using NuDepot.Core.Search;
using NuDepot.Core.Tests.Support;
using Xunit;

namespace NuDepot.Core.Tests;

public class RawAutocompleteClientTests(ProtocolFixture fixture) : IClassFixture<ProtocolFixture>
{
    private RawAutocompleteClient Target { get; } = fixture.AutocompleteClient;

    [Fact]
    public async Task GetDefaultAutocompleteResults()
    {
        AutocompleteResponse response = await Target.AutocompleteAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal(1, response.TotalHits);
        Assert.Equal("Test.Package", Assert.Single(response.Data));
    }

    [Fact]
    public async Task AddsAutocompleteParameters()
    {
        RecordingHttpMessageHandler handler = new(TestData.DefaultAutocomplete);
        RawAutocompleteClient client = new(new HttpClient(handler), TestData.AutocompleteUrl);

        await client.AutocompleteAsync("query", skip: 2, take: 5, includePrerelease: false, includeSemVer2: false, TestContext.Current.CancellationToken);

        Assert.Equal($"{TestData.AutocompleteUrl}?skip=2&take=5&q=query", Assert.Single(handler.RequestedUrls));
    }

    [Fact]
    public async Task ListsPackageVersions()
    {
        RecordingHttpMessageHandler handler = new("{ \"totalHits\": 1, \"data\": [ \"1.0.0\" ] }");
        RawAutocompleteClient client = new(new HttpClient(handler), TestData.AutocompleteUrl);

        AutocompleteResponse response = await client.ListPackageVersionsAsync("Test.Package", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal($"{TestData.AutocompleteUrl}?prerelease=True&semVerLevel=2.0.0&id=Test.Package", Assert.Single(handler.RequestedUrls));
        Assert.Equal(1, response.TotalHits);
        Assert.Equal("1.0.0", Assert.Single(response.Data));
    }

    [Fact]
    public async Task AddsListPackageVersionsParameters()
    {
        RecordingHttpMessageHandler handler = new("{ \"totalHits\": 0, \"data\": [] }");
        RawAutocompleteClient client = new(new HttpClient(handler), TestData.AutocompleteUrl);

        await client.ListPackageVersionsAsync("Test.Package", includePrerelease: false, includeSemVer2: false, TestContext.Current.CancellationToken);

        Assert.Equal($"{TestData.AutocompleteUrl}?id=Test.Package", Assert.Single(handler.RequestedUrls));
    }
}
