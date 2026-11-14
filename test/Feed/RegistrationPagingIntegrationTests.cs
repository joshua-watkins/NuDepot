using NuDepot.Core.Models;
using NuDepot.Core.PackageDetails;
using NuDepot.Feed.Tests.Support;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace NuDepot.Feed.Tests;

/// <summary>
/// A package with more versions than its registration index lists, so the index links to pages of them. The feed is
/// shared by the tests, since it takes a while to push that many versions.
/// </summary>
public class RegistrationPagingIntegrationTests(RegistrationPagingIntegrationTests.ManyVersionsFeed feed) : IClassFixture<RegistrationPagingIntegrationTests.ManyVersionsFeed>
{
    // 130 versions, 1.0.1 to 1.0.130, which aren't SemVer 2.0.0, and one that is.
    private const int Versions = 130;
    private const string SemVer2Version = "2.0.0-beta.1";

    private NuDepotTestApplication App => feed.App;

    [Fact]
    public async Task TheIndexLinksToPages()
    {
        using HttpClient client = App.CreateClient();

        JsonElement index = await client.GetFromJsonAsync<JsonElement>("v3/registration/many.versions/index.json", cancellationToken: TestContext.Current.CancellationToken);

        JsonElement[] pages = [.. index.GetProperty("items").EnumerateArray()];
        Assert.Equal(3, index.GetProperty("count").GetInt32());
        Assert.Equal(
            ["1.0.1-1.0.64", "1.0.65-1.0.128", "1.0.129-1.0.130"],
            pages.Select(p => $"{p.GetProperty("lower").GetString()}-{p.GetProperty("upper").GetString()}"));
        Assert.All(pages, p => Assert.False(p.TryGetProperty("items", out _)));
        Assert.Equal("http://localhost/v3/registration/many.versions/page/1.0.65/1.0.128.json", pages[1].GetProperty("@id").GetString());
    }

    [Theory]
    [InlineData("v3/registration/many.versions/index.json", Versions)]
    [InlineData("v3/registration-semver2/many.versions/index.json", Versions + 1)]
    public async Task EachPageListsItsVersions(string indexUrl, int expected)
    {
        using HttpClient client = App.CreateClient();
        JsonElement index = await client.GetFromJsonAsync<JsonElement>(indexUrl, cancellationToken: TestContext.Current.CancellationToken);

        List<string> versions = [];
        foreach (JsonElement link in index.GetProperty("items").EnumerateArray())
        {
            JsonElement page = await client.GetFromJsonAsync<JsonElement>(link.GetProperty("@id").GetString(), cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(link.GetProperty("@id").GetString(), page.GetProperty("@id").GetString());
            Assert.Equal(index.GetProperty("@id").GetString(), page.GetProperty("parent").GetString());
            Assert.Equal(link.GetProperty("count").GetInt32(), page.GetProperty("items").GetArrayLength());
            versions.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("catalogEntry").GetProperty("version").GetString()!));
        }

        Assert.Equal(expected, versions.Count);
        Assert.Equal(versions.Count, versions.Distinct().Count());
        Assert.Equal(expected > Versions, versions.Contains(SemVer2Version));
    }

    [Theory]
    [InlineData("v3/registration/many.versions/page/1.0.1/1.0.65.json")]
    [InlineData("v3/registration/many.versions/page/1.0.1/not-a-version.json")]
    [InlineData("v3/registration/missing.package/page/1.0.1/1.0.64.json")]
    // Packages with few versions have no pages.
    [InlineData("v3/registration/few.versions/page/1.0.0/1.0.0.json")]
    // The SemVer 2.0.0 version is only in that hive's pages.
    [InlineData("v3/registration/many.versions/page/1.0.129/2.0.0-beta.1.json")]
    public async Task OtherPagesAreNotFound(string url)
    {
        using HttpClient client = App.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("v3/registration/many.versions/index.json", true)]
    [InlineData("v3/registration-semver2/many.versions/index.json", true)]
    [InlineData("v3/registration-semver2/many.versions/page/1.0.1/1.0.64.json", true)]
    [InlineData("v3/registration/many.versions/1.0.1.json", true)]
    // Only the registrations are compressed.
    [InlineData("v3/search?q=many", false)]
    [InlineData("v3/package/many.versions/index.json", false)]
    public async Task RegistrationsAreGzipped(string url, bool gzipped)
    {
        using HttpClient client = App.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.AcceptEncoding.ParseAdd("gzip");

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(gzipped, response.Content.Headers.ContentEncoding.Contains("gzip"));
        if (gzipped)
        {
            using GZipStream json = new(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), CompressionMode.Decompress);
            Assert.Equal(JsonValueKind.Object, (await JsonDocument.ParseAsync(json, cancellationToken: TestContext.Current.CancellationToken)).RootElement.ValueKind);
        }
    }

    [Fact]
    public async Task PackagesWithFewVersionsListThemInTheIndex()
    {
        using HttpClient client = App.CreateClient();

        JsonElement index = await client.GetFromJsonAsync<JsonElement>("v3/registration/few.versions/index.json", cancellationToken: TestContext.Current.CancellationToken);

        JsonElement page = Assert.Single(index.GetProperty("items").EnumerateArray());
        Assert.Equal("http://localhost/v3/registration/few.versions/index.json#page/1.0.0/1.0.0", page.GetProperty("@id").GetString());
        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task TheOfficialNuGetClientReadsEveryVersion()
    {
        using HttpClient client = App.CreateClient();
        PackageSource source = new(new Uri(App.Server.BaseAddress, "v3/index.json").AbsoluteUri);
        SourceRepository repository = new(source, [new(() => new HttpSourceResourceProviderTestHost(client)), .. Repository.Provider.GetCoreV3()]);
        using SourceCacheContext cache = new() { NoCache = true, DirectDownload = true };

        PackageMetadataResource resource = (await repository.GetResourceAsync<PackageMetadataResource>(TestContext.Current.CancellationToken))!;
        IEnumerable<IPackageSearchMetadata> packages = await resource.GetMetadataAsync(
            "Many.Versions",
            includePrerelease: true,
            includeUnlisted: true,
            cache,
            NuGet.Common.NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(Versions + 1, packages.Count());
    }

    [Fact]
    public async Task NuDepotsClientsReadEveryVersion()
    {
        using HttpClient client = App.CreateClient();

        // The gallery's client, and the one mirrors use.
        RawPackageDetailsClient details = new(client, "http://localhost/v3/registration-semver2");
        IReadOnlyList<NuDepotPackageMetadata> gallery = await details.GetPackageMetadataAsync("Many.Versions", TestContext.Current.CancellationToken);
        Core.NuGetClient nuget = new(new Core.NuGetClientFactory(client, "http://localhost/v3/index.json"));
        IReadOnlyList<PackageMetadata> mirror = await nuget.GetPackageMetadataAsync("Many.Versions", TestContext.Current.CancellationToken);

        Assert.Equal(Versions + 1, gallery.Count);
        Assert.Equal(Versions + 1, mirror.Count);
        Assert.Equal("1.0.100", (await nuget.GetPackageMetadataAsync("Many.Versions", NuGet.Versioning.NuGetVersion.Parse("1.0.100"), TestContext.Current.CancellationToken)).Version);
    }

    /// <summary>
    /// The feed the tests share, with Many.Versions and Few.Versions.
    /// </summary>
    public sealed class ManyVersionsFeed : IAsyncLifetime
    {
        internal NuDepotTestApplication App { get; } = new(new NullTestOutputHelper());

        public async ValueTask InitializeAsync()
        {
            for (int i = 1; i <= Versions; i++)
                await App.AddPackageAsync(TestPackages.Create("Many.Versions", $"1.0.{i}"));

            await App.AddPackageAsync(TestPackages.Create("Many.Versions", SemVer2Version));
            await App.AddPackageAsync(TestPackages.Create("Few.Versions", "1.0.0"));
        }

        public ValueTask DisposeAsync()
        {
            App.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NullTestOutputHelper : ITestOutputHelper
    {
        public string Output => string.Empty;

        public void Write(string message) { }

        public void Write(string format, params object[] args) { }

        public void WriteLine(string message) { }

        public void WriteLine(string format, params object[] args) { }
    }
}
