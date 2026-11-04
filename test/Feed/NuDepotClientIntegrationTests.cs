using NuDepot.Core;
using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.PackageDetails;
using NuDepot.Core.Search;
using NuDepot.Core.ServiceIndex;
using NuDepot.Feed.Tests.Support;
using NuGet.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Uses NuDepot's client SDK to interact with the NuDepot test host.
/// </summary>
public class NuDepotClientIntegrationTests : IDisposable
{
    private Support.NuDepotTestApplication App { get; }
    private HttpClient HttpClient { get; }
    private NuGetClientFactory ClientFactory { get; }
    private NuGetClient Client { get; }

    private Stream PackageStream { get; }

    public NuDepotClientIntegrationTests(ITestOutputHelper output)
    {
        App = new Support.NuDepotTestApplication(output);

        Uri serviceIndexUrl = new(App.Server.BaseAddress, "v3/index.json");

        HttpClient = App.CreateClient();
        ClientFactory = new NuGetClientFactory(HttpClient, serviceIndexUrl.AbsoluteUri);
        Client = new NuGetClient(ClientFactory);

        PackageStream = TestResources.GetResourceStream(TestResources.Package);
    }

    [Fact]
    public async Task ValidIndex()
    {
        IServiceIndexClient client = ClientFactory.CreateServiceIndexClient();
        ServiceIndexResponse index = await client.GetAsync();

        Assert.Equal("3.0.0", index.Version);
        Assert.Equal(17, index.Resources.Count);

        Assert.NotEmpty(index.GetResourceUrl(["PackageBaseAddress/3.0.0"])!);
        Assert.NotEmpty(index.GetResourceUrl(["PackagePublish/2.0.0"])!);
        Assert.NotEmpty(index.GetResourceUrl(["RegistrationsBaseUrl"])!);
        Assert.NotEmpty(index.GetResourceUrl(["SearchAutocompleteService"])!);
        Assert.NotEmpty(index.GetResourceUrl(["SearchQueryService"])!);
        Assert.NotEmpty(index.GetResourceUrl(["SymbolPackagePublish/4.9.0"])!);
        Assert.NotEmpty(index.GetPackagePublishResourceUrl()!);
        Assert.NotEmpty(index.GetReadmeUriTemplate()!);
        Assert.NotEmpty(index.GetPackageDependentsResourceUrl()!);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("Dependency", 1)]
    [InlineData("DotnetTool", 0)]
    public async Task SearchFiltersByPackageType(string? packageType, int expectedResults)
    {
        await App.AddPackageAsync(PackageStream);

        ISearchClient client = ClientFactory.CreateSearchClient();
        SearchResponse response = await client.SearchAsync(packageType: packageType);

        Assert.Equal(expectedResults, response.Data.Count);
    }

    [Fact]
    public async Task PackageDetailsReturnsExtendedMetadata()
    {
        await App.AddPackageAsync(PackageStream);

        IPackageDetailsClient client = ClientFactory.CreatePackageDetailsClient();
        IReadOnlyList<NuDepotPackageMetadata> packages = await client.GetPackageMetadataAsync("TestData");

        NuDepotPackageMetadata package = Assert.Single(packages);

        Assert.Equal("TestData", package.PackageId);
        Assert.Equal("1.2.3", package.Version);
        Assert.Equal(0, package.Downloads);
        Assert.Contains("Dependency", package.PackageTypes!);
        Assert.True(package.Listed);
    }

    [Fact]
    public async Task PackageDetailsReturnsEmpty()
    {
        IPackageDetailsClient client = ClientFactory.CreatePackageDetailsClient();
        IReadOnlyList<NuDepotPackageMetadata> packages = await client.GetPackageMetadataAsync("PackageDoesNotExist");

        Assert.Empty(packages);
    }

    [Fact]
    public async Task PackageDetailsReadmeReturnsNullIfMissing()
    {
        IPackageDetailsClient client = ClientFactory.CreatePackageDetailsClient();
        Stream? readme = await client.GetPackageReadmeStreamOrNullAsync("PackageDoesNotExist", NuGetVersion.Parse("1.0.0"));

        Assert.Null(readme);
    }

    [Fact]
    public async Task PackageDetailsDependentsReturnsEmpty()
    {
        await App.AddPackageAsync(PackageStream);

        IPackageDetailsClient client = ClientFactory.CreatePackageDetailsClient();
        DependentsResponse dependents = await client.GetPackageDependentsAsync("TestData");

        Assert.Empty(dependents.Data);
        Assert.Equal(0, dependents.TotalHits);
    }

    [Fact]
    public async Task SearchReturnsResults()
    {
        await App.AddPackageAsync(PackageStream);

        IReadOnlyList<SearchResult> results = await Client.SearchAsync();

        SearchResult result = Assert.Single(results);
        string author = Assert.Single(result.Authors!);
        SearchResultVersion version = Assert.Single(result.Versions);

        Assert.Equal("TestData", result.PackageId);
        Assert.Equal("1.2.3", result.Version);
        Assert.Equal("Test description", result.Description);
        Assert.Equal("Test author", author);
        Assert.Equal(0, result.TotalDownloads);

        Assert.Equal("1.2.3", version.Version);
        Assert.Equal(0, version.Downloads);
    }

    [Fact]
    public async Task SearchReturnsEmpty()
    {
        IReadOnlyList<SearchResult> results = await Client.SearchAsync("PackageDoesNotExist");

        Assert.Empty(results);
    }

    [Fact]
    public async Task AutocompleteReturnsResults()
    {
        await App.AddPackageAsync(PackageStream);

        IReadOnlyList<string> results = await Client.AutocompleteAsync();

        string result = Assert.Single(results);

        Assert.Equal("TestData", result);
    }

    [Fact]
    public async Task AutocompleteReturnsEmpty()
    {
        IReadOnlyList<string> results = await Client.AutocompleteAsync("PackageDoesNotExist");

        Assert.Empty(results);
    }

    [Fact]
    public async Task AutocompleteVersions()
    {
        await App.AddPackageAsync(PackageStream);

        IAutocompleteClient client = ClientFactory.CreateAutocompleteClient();
        AutocompleteResponse results = await client.ListPackageVersionsAsync("TestData");

        string result = Assert.Single(results.Data);

        Assert.Equal(1, results.TotalHits);
        Assert.Equal("1.2.3", result);
    }

    [Fact]
    public async Task AutocompleteVersionsReturnsEmpty()
    {
        IAutocompleteClient client = ClientFactory.CreateAutocompleteClient();
        AutocompleteResponse results = await client.ListPackageVersionsAsync("PackageDoesNotExist");

        Assert.Empty(results.Data);
        Assert.Equal(0, results.TotalHits);
    }

    [Fact]
    public async Task VersionListReturnsResults()
    {
        await App.AddPackageAsync(PackageStream);

        IReadOnlyList<NuGetVersion> versions = await Client.ListPackageVersionsAsync("TestData");

        NuGetVersion version = Assert.Single(versions);

        Assert.Equal("1.2.3", version.ToNormalizedString());
    }

    [Fact]
    public async Task VersionListReturnsEmpty()
    {
        IReadOnlyList<NuGetVersion> versions = await Client.ListPackageVersionsAsync("PackageDoesNotExist");

        Assert.Empty(versions);
    }

    [Theory]
    [InlineData("TestData", "1.0.0", false)]
    [InlineData("TestData", "1.2.3", true)]
    [InlineData("PackageDoesNotExists", "1.0.0", false)]
    public async Task PackageDownloadWorks(string packageId, string packageVersion, bool exists)
    {
        await App.AddPackageAsync(PackageStream);

        try
        {
            NuGetVersion version = NuGetVersion.Parse(packageVersion);

            using MemoryStream memoryStream = new();
            using Stream packageStream = await Client.DownloadPackageAsync(packageId, version);

            await packageStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            Assert.True(exists);
            Assert.Equal(exists, memoryStream.Length > 0);
        }
        catch (PackageNotFoundException)
        {
            Assert.False(exists);
        }
    }

    [Theory]
    [InlineData("TestData", "1.0.0", false)]
    [InlineData("TestData", "1.2.3", true)]
    [InlineData("PackageDoesNotExists", "1.0.0", false)]
    public async Task ManifestDownloadWorks(string packageId, string packageVersion, bool exists)
    {
        await App.AddPackageAsync(PackageStream);

        try
        {
            NuGetVersion version = NuGetVersion.Parse(packageVersion);

            using MemoryStream memoryStream = new();
            using Stream packageStream = await Client.DownloadPackageManifestAsync(packageId, version);

            await packageStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            Assert.True(exists);
            Assert.Equal(exists, memoryStream.Length > 0);
        }
        catch (PackageNotFoundException)
        {
            Assert.False(exists);
        }
    }

    [Fact]
    public async Task PackageMetadataReturnsOk()
    {
        await App.AddPackageAsync(PackageStream);

        IReadOnlyList<PackageMetadata> packages = await Client.GetPackageMetadataAsync("TestData");

        PackageMetadata package = Assert.Single(packages);

        Assert.Equal("TestData", package.PackageId);
        Assert.Equal("1.2.3", package.Version);
        Assert.Equal("Test description", package.Description);
        Assert.Equal("Test author", package.Authors);
        Assert.True(package.Listed);
    }

    [Fact]
    public async Task PackageMetadataReturnsEmty()
    {
        IReadOnlyList<PackageMetadata> packages = await Client.GetPackageMetadataAsync("PackageDoesNotExist");

        Assert.Empty(packages);
    }

    public void Dispose()
    {
        App.Dispose();
        HttpClient.Dispose();
    }
}
