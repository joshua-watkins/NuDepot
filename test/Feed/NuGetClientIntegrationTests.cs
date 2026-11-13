using NuDepot.Feed.Tests.Support;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Uses the official NuGet client to interact with the NuDepot test host.
/// </summary>
public sealed class NuGetClientIntegrationTests : IDisposable
{
    private NuDepotTestApplication App { get; }
    private HttpClient Client { get; }

    private Stream PackageStream { get; }

    private SourceRepository Repository { get; }
    private SourceCacheContext Cache { get; }
    private NuGet.Common.ILogger Logger { get; }
    private CancellationToken CancellationToken { get; }

    public NuGetClientIntegrationTests(ITestOutputHelper output)
    {
        App = new NuDepotTestApplication(output);
        Client = App.CreateClient();
        PackageStream = TestResources.GetResourceStream(TestResources.Package);

        Uri sourceUri = new(App.Server.BaseAddress, "v3/index.json");
        PackageSource packageSource = new(sourceUri.AbsoluteUri);
        List<Lazy<INuGetResourceProvider>> providers =
        [
            new(() => new HttpSourceResourceProviderTestHost(Client)), .. NuGet.Protocol.Core.Types.Repository.Provider.GetCoreV3()
        ];

        Repository = new SourceRepository(packageSource, providers);
        Cache = new SourceCacheContext { NoCache = true, MaxAge = new DateTimeOffset(), DirectDownload = true };
        Logger = NuGet.Common.NullLogger.Instance;
        CancellationToken = CancellationToken.None;
    }

    [Fact]
    public async Task ValidIndex()
    {
        ServiceIndexResourceV3 index = await GetResourceAsync<ServiceIndexResourceV3>();

        Assert.Equal(17, index.Entries.Count);

        Assert.NotEmpty(index.GetServiceEntries("PackageBaseAddress/3.0.0"));
        Assert.NotEmpty(index.GetServiceEntries("PackagePublish/2.0.0"));
        Assert.NotEmpty(index.GetServiceEntries("RegistrationsBaseUrl"));
        Assert.NotEmpty(index.GetServiceEntries("SearchAutocompleteService"));
        Assert.NotEmpty(index.GetServiceEntries("SearchQueryService"));
        Assert.NotEmpty(index.GetServiceEntries("SymbolPackagePublish/4.9.0"));
        Assert.NotEmpty(index.GetServiceEntries("ReadmeUriTemplate/6.13.0"));
    }

    [Fact]
    public async Task SearchReturnsResults()
    {
        await App.AddPackageAsync(PackageStream);

        PackageSearchResource resource = await GetResourceAsync<PackageSearchResource>();
        SearchFilter searchFilter = new(includePrerelease: true);

        IEnumerable<IPackageSearchMetadata> results = await resource.SearchAsync(
            "",
            searchFilter,
            skip: 0,
            take: 20,
            Logger,
            CancellationToken);

        IPackageSearchMetadata result = Assert.Single(results);

        Assert.Equal("TestData", result.Identity.Id);
        Assert.Equal("1.2.3", result.Identity.Version.ToNormalizedString());
        Assert.Equal("Test description", result.Description);
        Assert.Equal("Test author", result.Authors);
        Assert.Equal(0, result.DownloadCount);

        IEnumerable<VersionInfo> versions = await result.GetVersionsAsync();
        VersionInfo version = Assert.Single(versions);

        Assert.Equal("1.2.3", version.Version.ToNormalizedString());
        Assert.Equal(0, version.DownloadCount);
    }

    [Fact]
    public async Task SearchReturnsEmpty()
    {
        PackageSearchResource resource = await GetResourceAsync<PackageSearchResource>();
        SearchFilter searchFilter = new(includePrerelease: true);

        IEnumerable<IPackageSearchMetadata> results = await resource.SearchAsync(
            "PackageDoesNotExist",
            searchFilter,
            skip: 0,
            take: 20,
            Logger,
            CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task AutocompleteReturnsResults()
    {
        await App.AddPackageAsync(PackageStream);

        AutoCompleteResource resource = await GetResourceAsync<AutoCompleteResource>();
        IEnumerable<string> results = await resource.IdStartsWith(
            "",
            includePrerelease: true,
            Logger,
            CancellationToken);

        string result = Assert.Single(results);

        Assert.Equal("TestData", result);
    }

    [Fact]
    public async Task AutocompleteReturnsEmpty()
    {
        AutoCompleteResource resource = await GetResourceAsync<AutoCompleteResource>();
        IEnumerable<string> results = await resource.IdStartsWith(
            "PackageDoesNotExist",
            includePrerelease: true,
            Logger,
            CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task VersionListReturnsResults()
    {
        await App.AddPackageAsync(PackageStream);

        FindPackageByIdResource resource = await GetResourceAsync<FindPackageByIdResource>();
        IEnumerable<NuGetVersion> versions = await resource.GetAllVersionsAsync(
            "TestData",
            Cache,
            Logger,
            CancellationToken);

        NuGetVersion version = Assert.Single(versions);

        Assert.Equal("1.2.3", version.ToNormalizedString());
    }

    [Fact]
    public async Task VersionListReturnsEmpty()
    {
        FindPackageByIdResource resource = await GetResourceAsync<FindPackageByIdResource>();
        IEnumerable<NuGetVersion> versions = await resource.GetAllVersionsAsync(
            "PackageDoesNotExist",
            Cache,
            Logger,
            CancellationToken);

        Assert.Empty(versions);
    }

    [Theory]
    [InlineData("TestData", "1.0.0", false)]
    [InlineData("TestData", "1.2.3", true)]
    [InlineData("PackageDoesNotExists", "1.0.0", false)]
    public async Task PackageExistsWorks(string packageId, string packageVersion, bool exists)
    {
        await App.AddPackageAsync(PackageStream);

        NuGetVersion version = NuGetVersion.Parse(packageVersion);
        FindPackageByIdResource resource = await GetResourceAsync<FindPackageByIdResource>();
        bool result = await resource.DoesPackageExistAsync(
            packageId,
            version,
            Cache,
            Logger,
            CancellationToken);

        Assert.Equal(exists, result);
    }

    [Theory]
    [InlineData("TestData", "1.0.0", false)]
    [InlineData("TestData", "1.2.3", true)]
    [InlineData("PackageDoesNotExists", "1.0.0", false)]
    public async Task PackageDownloadWorks(string packageId, string packageVersion, bool exists)
    {
        await App.AddPackageAsync(PackageStream);

        using MemoryStream packageStream = new();

        NuGetVersion version = NuGetVersion.Parse(packageVersion);
        FindPackageByIdResource resource = await GetResourceAsync<FindPackageByIdResource>();
        bool result = await resource.CopyNupkgToStreamAsync(
            packageId,
            version,
            packageStream,
            Cache,
            Logger,
            CancellationToken);

        packageStream.Position = 0;

        Assert.Equal(exists, result);
        Assert.Equal(exists, packageStream.Length > 0);
    }

    [Fact]
    public async Task PackageMetadataReturnsOk()
    {
        await App.AddPackageAsync(PackageStream);

        PackageMetadataResource resource = await GetResourceAsync<PackageMetadataResource>();
        IEnumerable<IPackageSearchMetadata> packages = await resource.GetMetadataAsync(
            "TestData",
            includePrerelease: true,
            includeUnlisted: true,
            Cache,
            Logger,
            CancellationToken);

        IPackageSearchMetadata package = Assert.Single(packages);

        Assert.Equal("TestData", package.Identity.Id);
        Assert.Equal("1.2.3", package.Identity.Version.ToNormalizedString());
        Assert.Equal("Test description", package.Description);
        Assert.Equal("Test author", package.Authors);
        Assert.True(package.IsListed);
    }

    [Fact]
    public async Task PackageMetadataReturnsEmty()
    {
        PackageMetadataResource resource = await GetResourceAsync<PackageMetadataResource>();
        IEnumerable<IPackageSearchMetadata> packages = await resource.GetMetadataAsync(
            "PackageDoesNotExist",
            includePrerelease: true,
            includeUnlisted: true,
            Cache,
            Logger,
            CancellationToken);

        Assert.Empty(packages);
    }

    public void Dispose()
    {
        App.Dispose();
        Client.Dispose();
        Cache.Dispose();
    }

    private async Task<TResource> GetResourceAsync<TResource>()
        where TResource : class, INuGetResource
    {
        return await Repository.GetResourceAsync<TResource>()
            ?? throw new InvalidOperationException($"The feed doesn't have the {typeof(TResource).Name} resource.");
    }
}
