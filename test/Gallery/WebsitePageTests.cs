using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NuDepot.Core.Models;
using NuDepot.Core.PackageDetails;
using NuDepot.Core.RepositorySignatures;
using NuDepot.Core.Search;
using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace NuDepot.Gallery.Tests;

/// <summary>
/// Renders the website's pages through the website application, with fake feed clients.
/// </summary>
public sealed partial class WebsitePageTests : IDisposable
{
    private const string ServiceIndexUrl = "https://feed.test/v3/index.json";

    private Mock<ISearchClient> Search { get; } = new();
    private Mock<IPackageDetailsClient> Packages { get; } = new();
    private Mock<IRepositorySignaturesClient> RepositorySignatures { get; } = new();
    private WebApplication App { get; }

    public WebsitePageTests()
    {
        // Host the gallery in memory, the way the Windows and Linux hosts do, without the feed.
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Gallery:ServiceIndexUrl", ServiceIndexUrl },
        });

        builder.Services.AddNuDepotGallery();
        builder.Services.AddSingleton(Search.Object);
        builder.Services.AddSingleton(Packages.Object);
        builder.Services.AddSingleton(RepositorySignatures.Object);

        App = builder.Build();
        App.UseNuDepotGallery();
        App
            .StartAsync()
            .GetAwaiter()
            .GetResult();

        Search
            .Setup(s => s.SearchAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponse { Data = [] });
    }

    public void Dispose() => App
        .DisposeAsync()
        .AsTask()
        .GetAwaiter()
        .GetResult();

    [Fact]
    public async Task SearchPageListsPackages()
    {
        Search
            .Setup(s => s.SearchAsync("json", 0, 20, true, true, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponse
            {
                Data =
                [
                    new() {
                        PackageId = "Contoso.Json",
                        Version = "1.2.0",
                        Description = "Reads <json> & writes it.",
                        Authors = ["Contoso"],
                        Tags = ["json", "serialization"],
                        TotalDownloads = 1500,
                        Versions = [],
                    },
                ],
            });

        string html = await GetPageAsync("/?q=json");

        Assert.Contains("<title>Packages - NuDepot</title>", html);
        Assert.Contains("href=\"/packages/Contoso.Json/1.2.0\"", html);
        Assert.Contains("1.5k total downloads", html);
        Assert.Contains("Reads &lt;json&gt; &amp; writes it.", html);
        Assert.Contains("value=\"json\"", html);
        Assert.Contains("class=\"active\"><span>Packages</span>", html);
    }

    [Fact]
    public async Task SearchPageKeepsFiltersInLinks()
    {
        await GetPageAsync("/?q=json&p=2&packageType=dotnettool&prerelease=False");

        Search.Verify(s => s.SearchAsync("json", 20, 20, false, true, "dotnettool", null, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task SearchPageRejectsInvalidPage()
    {
        using HttpClient client = App.GetTestClient();

        HttpResponseMessage response = await client.GetAsync("/?p=0", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PackagePageShowsDetails()
    {
        Packages
            .Setup(p => p.GetPackageMetadataAsync("Contoso.Json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new() {
                    PackageId = "Contoso.Json",
                    Version = "1.2.0",
                    Description = "Reads JSON.",
                    Authors = "Contoso",
                    Listed = true,
                    Published = DateTimeOffset.UtcNow,
                    PackageContentUrl = "https://feed.test/v3/package/contoso.json/1.2.0/contoso.json.1.2.0.nupkg",
                    Deprecation = new PackageDeprecation
                    {
                        Reasons = ["Legacy"],
                        AlternatePackage = new AlternatePackage { Id = "Contoso.Text.Json", Range = "*" },
                    },
                },
            ]);
        Packages
            .Setup(p => p.GetPackageDependentsAsync("Contoso.Json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DependentsResponse { Data = [] });

        string html = await GetPageAsync("/packages/Contoso.Json/1.2.0");

        Assert.Contains("<title>Contoso.Json 1.2.0 - NuDepot</title>", html);
        Assert.Contains("This package is no longer maintained.", html);
        Assert.Contains("href=\"/packages/Contoso.Text.Json\"", html);
        Assert.Contains("href=\"https://feed.test/v3/package/contoso.json/1.2.0/contoso.json.1.2.0.nupkg\"", html);
        Assert.Contains("<span>Versions</span>", html);

        // The install commands are in the page, so it shows them without scripts, encoded as HTML.
        Assert.Contains(">dotnet add package Contoso.Json --version 1.2.0<", html);
        Assert.Contains("&lt;PackageReference Include=&quot;Contoso.Json&quot; Version=&quot;1.2.0&quot; /&gt;", html);
    }

    [Fact]
    public async Task PackagePageShowsMissingPackage()
    {
        Packages
            .Setup(p => p.GetPackageMetadataAsync("Missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        string html = await GetPageAsync("/packages/Missing");

        Assert.Contains("Could not find package 'Missing'.", html);
    }

    [Fact]
    public async Task UploadPageShowsPushCommands()
    {
        string html = await GetPageAsync("/upload");

        Assert.Contains($"<code>{ServiceIndexUrl}</code>", html);
        Assert.Contains($">dotnet nuget push -s {ServiceIndexUrl} package.nupkg<", html);
        Assert.Contains($">Register-PSResourceRepository -Name NuDepot -Uri {ServiceIndexUrl} -Trusted<", html);
        Assert.Contains(">Publish-PSResource -Path ./MyModule -Repository NuDepot<", html);
        Assert.Contains("class=\"active\"><span>Upload</span>", html);

        // The feed doesn't sign packages.
        Assert.DoesNotContain("trustedSigners", html);
    }

    [Fact]
    public async Task UploadPageShowsTheFeedsSigningCertificates()
    {
        RepositorySignatures
            .Setup(s => s.GetOrNullAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepositorySignaturesResponse
            {
                SigningCertificates =
                [
                    Certificate("aa11", "CN=NuDepot Repository Signing 2030", 2030),
                    Certificate("bb22", "CN=NuDepot Repository Signing 2031", 2031),
                ],
            });

        string html = await GetPageAsync("/upload");

        Assert.Contains("<code>aa11</code>", html);
        Assert.Contains("<code>bb22</code>", html);
        Assert.Contains("2031-01-01 to 2031-12-31", html);
        Assert.Contains($"&lt;repository name=&quot;NuDepot&quot; serviceIndex=&quot;{ServiceIndexUrl}&quot;&gt;", html);
        Assert.Contains("&lt;certificate fingerprint=&quot;bb22&quot; hashAlgorithm=&quot;SHA256&quot; allowUntrustedRoot=&quot;true&quot; /&gt;", html);

        static RepositorySigningCertificate Certificate(string fingerprint, string subject, int year) => new()
        {
            ContentUrl = $"https://feed.test/v3/repository-signatures/certificates/{fingerprint}.crt",
            Fingerprints = new Dictionary<string, string> { { RepositorySigningCertificate.Sha256Oid, fingerprint } },
            Subject = subject,
            Issuer = subject,
            NotBefore = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero),
            NotAfter = new DateTimeOffset(year, 12, 31, 23, 59, 59, TimeSpan.Zero),
        };
    }

    [Fact]
    public async Task ExplainsWhenFeedIsUnreachable()
    {
        Search
            .Setup(s => s.SearchAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        using HttpClient client = App.GetTestClient();
        HttpResponseMessage response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("the feed isn't responding", html);
        Assert.Contains($"<code>{ServiceIndexUrl}</code>", html);
    }

    [Theory]
    [InlineData("/_content/NuDepot.Gallery/css/site.css", "text/css")]
    [InlineData("/_content/NuDepot.Gallery/lib/bootstrap/dist/css/bootstrap.min.css", "text/css")]
    [InlineData("/_content/NuDepot.Gallery/lib/bootstrap-icons/icons/search.svg", "image/svg+xml")]
    [InlineData("/_content/NuDepot.Gallery/js/site.js", "text/javascript")]
    [InlineData("/_content/NuDepot.Gallery/images/default-package-icon-256x256.png", "image/png")]
    public async Task ServesEmbeddedFiles(string url, string contentType)
    {
        using HttpClient client = App.GetTestClient();

        HttpResponseMessage response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmbeddedFilesAreNotModifiedForTheSameETag()
    {
        using HttpClient client = App.GetTestClient();
        HttpResponseMessage first = await client.GetAsync("/_content/NuDepot.Gallery/css/site.css", TestContext.Current.CancellationToken);

        using HttpRequestMessage request = new(HttpMethod.Get, "/_content/NuDepot.Gallery/css/site.css");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        HttpResponseMessage second = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Theory]
    [InlineData("/_content/NuDepot.Gallery/css/missing.css")]
    [InlineData("/_content/NuDepot.Gallery/css")]
    public async Task MissingEmbeddedFilesAreNotFound(string url)
    {
        using HttpClient client = App.GetTestClient();

        HttpResponseMessage response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/account")]
    [InlineData("/account/signin")]
    public async Task AccountPagesNeedAFeedWithUsers(string url)
    {
        using HttpClient client = App.GetTestClient();

        HttpResponseMessage response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoSignInLinkWithoutUsers()
    {
        string html = await GetPageAsync("/");

        Assert.DoesNotContain("account/signin", html);
        Assert.DoesNotContain("account-nav", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/upload")]
    public async Task PagesCantBeFramed(string url)
    {
        using HttpClient client = App.GetTestClient();

        HttpResponseMessage response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal(GalleryApplicationBuilderExtensions.PagePolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("strict-origin-when-cross-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/upload")]
    [InlineData("/packages/Contoso.Json/1.2.0")]
    public async Task PagesHaveNoInlineScripts(string url)
    {
        Packages
            .Setup(p => p.GetPackageMetadataAsync("Contoso.Json", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new() { PackageId = "Contoso.Json", Version = "1.2.0", Listed = true, Published = DateTimeOffset.UtcNow }]);
        Packages
            .Setup(p => p.GetPackageDependentsAsync("Contoso.Json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DependentsResponse { Data = [] });

        string html = await GetPageAsync(url);

        // The Content-Security-Policy would block them: scripts are files, and settings are JSON, which doesn't run.
        Assert.DoesNotMatch(InlineScriptPattern(), html);
        Assert.DoesNotMatch(EventHandlerPattern(), html);
        Assert.Contains("js/site.js\"></script>", html);
    }

    [Fact]
    public async Task EmbeddedFilesAreNotSniffed()
    {
        using HttpClient client = App.GetTestClient();

        HttpResponseMessage response = await client.GetAsync("/_content/NuDepot.Gallery/css/site.css", TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    private async Task<string> GetPageAsync(string url)
    {
        using HttpClient client = App.GetTestClient();
        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        return await response.Content.ReadAsStringAsync();
    }

    // A script element that isn't a file or JSON settings.
    [GeneratedRegex("<script(?![^>]*\\bsrc=)(?![^>]*type=\"application/json\")", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScriptPattern();

    // An event handler attribute, like onclick.
    [GeneratedRegex("\\son[a-z]+\\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandlerPattern();
}
