using Microsoft.Extensions.DependencyInjection;
using NuDepot.Feed.Tests.Support;
using System.Net;
using Xunit;

namespace NuDepot.Feed.Tests;

public sealed class MirrorIntegrationTests : IDisposable
{
    private NuDepotTestApplication Upstream { get; }
    private NuDepotTestApplication Downstream { get; }
    private HttpClient DownstreamClient { get; }
    private Stream PackageStream { get; }

    public MirrorIntegrationTests(ITestOutputHelper output)
    {
        // Fixed clocks, so the packages' published times are always the same.
        Upstream = new NuDepotTestApplication(output, configureServices: FixClock);
        Downstream = new NuDepotTestApplication(output, Upstream.CreateClient(), configureServices: FixClock);

        DownstreamClient = Downstream.CreateClient();
        PackageStream = TestResources.GetResourceStream(TestResources.Package);
    }

    [Fact]
    public async Task SearchExcludesUpstream()
    {
        await Upstream.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage downstreamResponse = await DownstreamClient.GetAsync("v3/search", TestContext.Current.CancellationToken);
        Stream downstreamContent = await downstreamResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        string downstreamJson = downstreamContent.ToPrettifiedJson();

        // The downstream package source should not have the package.
        Assert.Equal(HttpStatusCode.OK, downstreamResponse.StatusCode);
        Assert.Equal(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#"",
    ""@base"": ""http://localhost/v3/registration""
  },
  ""totalHits"": 0,
  ""data"": []
}", downstreamJson);
    }

    [Fact]
    public async Task VersionListIncludesUpstream()
    {
        await Upstream.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);

        HttpResponseMessage response = await DownstreamClient.GetAsync("v3/package/TestData/index.json", TestContext.Current.CancellationToken);
        string content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(@"{""versions"":[""1.2.3""]}", content);
    }

    [Fact]
    public async Task PackageDownloadIncludesUpstream()
    {
        await Upstream.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await DownstreamClient.GetAsync("v3/package/TestData/1.2.3/TestData.1.2.3.nupkg", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NuspecDownloadIncludesUpstream()
    {
        await Upstream.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await DownstreamClient.GetAsync("v3/package/TestData/1.2.3/TestData.1.2.3.nuspec", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PackageMetadataIncludesUpstream()
    {
        await Upstream.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await DownstreamClient.GetAsync("v3/registration/TestData/index.json", TestContext.Current.CancellationToken);
        Stream content = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        string json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(@"{
  ""@id"": ""http://localhost/v3/registration/testdata/index.json"",
  ""@type"": [
    ""catalog:CatalogRoot"",
    ""PackageRegistration"",
    ""catalog:Permalink""
  ],
  ""count"": 1,
  ""items"": [
    {
      ""@id"": ""http://localhost/v3/registration/testdata/index.json#page/1.2.3/1.2.3"",
      ""count"": 1,
      ""lower"": ""1.2.3"",
      ""upper"": ""1.2.3"",
      ""items"": [
        {
          ""@id"": ""http://localhost/v3/registration/testdata/1.2.3.json"",
          ""packageContent"": ""http://localhost/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg"",
          ""catalogEntry"": {
            ""downloads"": 0,
            ""hasReadme"": false,
            ""packageTypes"": [],
            ""repositoryUrl"": """",
            ""@id"": ""http://localhost/v3/registration/testdata/1.2.3.json"",
            ""id"": ""TestData"",
            ""version"": ""1.2.3"",
            ""authors"": ""Test author"",
            ""dependencyGroups"": [
              {
                ""targetFramework"": ""net5.0"",
                ""dependencies"": []
              }
            ],
            ""description"": ""Test description"",
            ""iconUrl"": """",
            ""language"": """",
            ""licenseUrl"": """",
            ""listed"": true,
            ""minClientVersion"": """",
            ""packageContent"": ""http://localhost/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg"",
            ""projectUrl"": """",
            ""published"": ""2020-01-01T00:00:00+00:00"",
            ""requireLicenseAcceptance"": false,
            ""summary"": """",
            ""tags"": [],
            ""title"": """"
          }
        }
      ]
    }
  ],
  ""totalDownloads"": 0
}", json);
    }

    [Fact]
    public async Task PackageMetadataLeafIncludesUpstream()
    {
        await Upstream.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await DownstreamClient.GetAsync("v3/registration/TestData/1.2.3.json", TestContext.Current.CancellationToken);
        Stream content = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        string json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(@"{
  ""@id"": ""http://localhost/v3/registration/testdata/1.2.3.json"",
  ""@type"": [
    ""Package"",
    ""http://schema.nuget.org/catalog#Permalink""
  ],
  ""listed"": true,
  ""packageContent"": ""http://localhost/v3/package/testdata/1.2.3/testdata.1.2.3.nupkg"",
  ""published"": ""2020-01-01T00:00:00+00:00"",
  ""registration"": ""http://localhost/v3/registration/testdata/index.json"",
  ""catalogEntry"": ""http://localhost/v3/catalog/data/1/testdata.1.2.3.json""
}", json);
    }

    public void Dispose()
    {
        Upstream.Dispose();
        Downstream.Dispose();
    }

    private static void FixClock(IServiceCollection services)
        => services.AddSingleton<TimeProvider>(new TestTimeProvider(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)));
}
