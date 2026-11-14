using Microsoft.Extensions.DependencyInjection;
using NuDepot.Feed.Tests.Support;
using System.Net;
using Xunit;

namespace NuDepot.Feed.Tests;

public sealed class ApiIntegrationTests : IDisposable
{
    private NuDepotTestApplication App { get; }
    private HttpClient Client { get; }

    private Stream PackageStream { get; }
    private Stream SymbolPackageStream { get; }

    public ApiIntegrationTests(ITestOutputHelper output)
    {
        // A fixed clock, so the packages' published times are always the same.
        App = new NuDepotTestApplication(output, configureServices: services => services.AddSingleton<TimeProvider>(new TestTimeProvider(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero))));
        Client = App.CreateClient();

        PackageStream = TestResources.GetResourceStream(TestResources.Package);
        SymbolPackageStream = TestResources.GetResourceStream(TestResources.SymbolPackage);
    }

    [Fact]
    public async Task IndexReturnsOk()
    {
        using HttpResponseMessage response = await Client.GetAsync("v3/index.json", TestContext.Current.CancellationToken);
        string content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestData.ServiceIndex, content);
    }

    [Fact]
    public async Task SearchReturnsOk()
    {
        await App.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await Client.GetAsync("v3/search", TestContext.Current.CancellationToken);
        Stream content = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        string json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#"",
    ""@base"": ""http://localhost/v3/registration""
  },
  ""totalHits"": 1,
  ""data"": [
    {
      ""id"": ""TestData"",
      ""version"": ""1.2.3"",
      ""description"": ""Test description"",
      ""authors"": [
        ""Test author""
      ],
      ""iconUrl"": """",
      ""licenseUrl"": """",
      ""packageTypes"": [
        {
          ""name"": ""Dependency""
        }
      ],
      ""projectUrl"": """",
      ""registration"": ""http://localhost/v3/registration/testdata/index.json"",
      ""summary"": """",
      ""tags"": [],
      ""title"": """",
      ""totalDownloads"": 0,
      ""versions"": [
        {
          ""@id"": ""http://localhost/v3/registration/testdata/1.2.3.json"",
          ""version"": ""1.2.3"",
          ""downloads"": 0
        }
      ]
    }
  ]
}", json);
    }

    [Fact]
    public async Task SearchReturnsEmpty()
    {
        using HttpResponseMessage response = await Client.GetAsync("v3/search?q=PackageDoesNotExist", TestContext.Current.CancellationToken);
        Stream content = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        string json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#"",
    ""@base"": ""http://localhost/v3/registration""
  },
  ""totalHits"": 0,
  ""data"": []
}", json);
    }

    [Fact]
    public async Task AutocompleteReturnsOk()
    {
        await App.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await Client.GetAsync("v3/autocomplete", TestContext.Current.CancellationToken);
        Stream content = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        string json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#""
  },
  ""totalHits"": 1,
  ""data"": [
    ""TestData""
  ]
}", json);
    }

    [Fact]
    public async Task AutocompleteReturnsEmpty()
    {
        using HttpResponseMessage response = await Client.GetAsync("v3/autocomplete?q=PackageDoesNotExist", TestContext.Current.CancellationToken);
        Stream content = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        string json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#""
  },
  ""totalHits"": 0,
  ""data"": []
}", json);
    }

    [Fact]
    public async Task AutocompleteVersionsReturnsOk()
    {
        await App.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await Client.GetAsync("v3/autocomplete?id=TestData", TestContext.Current.CancellationToken);
        Stream content = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        string json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#""
  },
  ""totalHits"": 1,
  ""data"": [
    ""1.2.3""
  ]
}", json);
    }

    [Fact]
    public async Task AutocompleteVersionsReturnsEmpty()
    {
        using HttpResponseMessage response = await Client.GetAsync("v3/autocomplete?id=PackageDoesNotExist", TestContext.Current.CancellationToken);
        Stream content = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        string json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(@"{
  ""@context"": {
    ""@vocab"": ""http://schema.nuget.org/schema#""
  },
  ""totalHits"": 0,
  ""data"": []
}", json);
    }

    [Fact]
    public async Task VersionListReturnsOk()
    {
        await App.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);

        HttpResponseMessage response = await Client.GetAsync("v3/package/TestData/index.json", TestContext.Current.CancellationToken);
        string content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(@"{""versions"":[""1.2.3""]}", content);
    }

    [Fact]
    public async Task VersionListReturnsNotFound()
    {
        using HttpResponseMessage response = await Client.GetAsync("v3/package/PackageDoesNotExist/index.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PackageDownloadReturnsOk()
    {
        await App.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await Client.GetAsync("v3/package/TestData/1.2.3/TestData.1.2.3.nupkg", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PackageDownloadReturnsNotFound()
    {
        using HttpResponseMessage response = await Client.GetAsync("v3/package/PackageDoesNotExist/1.0.0/PackageDoesNotExist.1.0.0.nupkg", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NuspecDownloadReturnsOk()
    {
        await App.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await Client.GetAsync("v3/package/TestData/1.2.3/TestData.1.2.3.nuspec", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NuspecDownloadReturnsNotFound()
    {
        using HttpResponseMessage response = await Client.GetAsync("v3/package/PackageDoesNotExist/1.0.0/PackageDoesNotExist.1.0.0.nuspec", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PackageMetadataReturnsOk()
    {
        await App.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await Client.GetAsync("v3/registration/TestData/index.json", TestContext.Current.CancellationToken);
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
            ""packageTypes"": [
              ""Dependency""
            ],
            ""releaseNotes"": """",
            ""repositoryUrl"": """",
            ""@id"": ""http://localhost/v3/catalog/data/1/testdata.1.2.3.json"",
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
    public async Task PackageMetadataReturnsNotFound()
    {
        using HttpResponseMessage response = await Client.GetAsync("v3/registration/PackageDoesNotExist/index.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PackageMetadataLeafReturnsOk()
    {
        await App.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await Client.GetAsync("v3/registration/TestData/1.2.3.json", TestContext.Current.CancellationToken);
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

    [Fact]
    public async Task PackageMetadataLeafReturnsNotFound()
    {
        using HttpResponseMessage response = await Client.GetAsync("v3/registration/PackageDoesNotExist/1.0.0.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PackageDependentsReturnsOk()
    {
        using HttpResponseMessage response = await Client.GetAsync("v3/dependents?packageId=TestData", TestContext.Current.CancellationToken);

        Stream content = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        string json = content.ToPrettifiedJson();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(@"{
  ""totalHits"": 0,
  ""data"": []
}", json);
    }

    [Fact]
    public async Task PackageDependentsReturnsBadRequest()
    {
        using HttpResponseMessage response = await Client.GetAsync("v3/dependents", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SymbolDownloadReturnsOk()
    {
        await App.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);
        await App.AddSymbolPackageAsync(SymbolPackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await Client.GetAsync("api/download/symbols/testdata.pdb/16F71ED8DD574AA2AD4A22D29E9C981Bffffffff/testdata.pdb", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("api/download/symbols/testdata.pdb/16F71ED8DD574AA2AD4A22D29E9C981B1/testdata.pdb")]
    [InlineData("api/download/symbols/testdata.pdb/16F71ED8DD574AA2AD4A22D29E9C981B/testdata.pdb")]
    [InlineData("api/download/symbols/testprefix/testdata.pdb/16F71ED8DD574AA2AD4A22D29E9C981Bffffffff/testdata.pdb")]
    public async Task MalformedSymbolDownloadReturnsOk(string uri)
    {
        await App.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);
        await App.AddSymbolPackageAsync(SymbolPackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await Client.GetAsync(uri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("api/download/symbols/testdata.pdb/16F71ED8/testdata.pdb")]
    [InlineData("api/download/symbols/testdata.pdb/16F71ED8DD574AA2AD4A22D29E9C981B-fffffff/testdata.pdb")]
    [InlineData("api/download/symbols/%2E%2E/16F71ED8DD574AA2AD4A22D29E9C981Bffffffff/testdata.pdb")]
    [InlineData("api/download/symbols/..%5Ctestdata.pdb/16F71ED8DD574AA2AD4A22D29E9C981Bffffffff/testdata.pdb")]
    public async Task InvalidSymbolDownloadReturnsNotFound(string uri)
    {
        await App.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);
        await App.AddSymbolPackageAsync(SymbolPackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await Client.GetAsync(uri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("v3/package/TestData/1.2.3/TestData.1.2.3.nupkg")]
    [InlineData("v3/package/TestData/1.2.3/TestData.1.2.3.nuspec")]
    public async Task PackageContentIsSandboxed(string uri)
    {
        await App.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await Client.GetAsync(uri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        string policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("sandbox", policy);
        Assert.Contains("default-src 'none'", policy);
    }

    [Fact]
    public async Task SymbolDownloadIsNotSniffed()
    {
        await App.AddPackageAsync(PackageStream, cancellationToken: TestContext.Current.CancellationToken);
        await App.AddSymbolPackageAsync(SymbolPackageStream, cancellationToken: TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await Client.GetAsync("api/download/symbols/testdata.pdb/16F71ED8DD574AA2AD4A22D29E9C981Bffffffff/testdata.pdb", TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task SymbolDownloadReturnsNotFound()
    {
        using HttpResponseMessage response = await Client.GetAsync("api/download/symbols/doesnotexist.pdb/16F71ED8DD574AA2AD4A22D29E9C981Bffffffff/doesnotexist.pdb", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        App.Dispose();
        Client.Dispose();
    }
}
