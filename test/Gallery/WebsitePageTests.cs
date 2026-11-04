using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NuDepot.Core.Models;
using NuDepot.Core.PackageDetails;
using NuDepot.Core.Search;
using NuDepot.Core.ServiceIndex;
using NuDepot.Gallery.Extensions;
using NuGet.Versioning;
using Xunit;

namespace NuDepot.Gallery.Tests;

/// <summary>
/// Renders the website's pages through the website application, with fake feed clients.
/// </summary>
public class WebsitePageTests : IDisposable
{
    private const string ServiceIndexUrl = "https://feed.test/v3/index.json";

    private readonly Mock<ISearchClient> _search = new();
    private readonly Mock<IPackageDetailsClient> _packages = new();
    private readonly Mock<IServiceIndexClient> _serviceIndex = new();
    private readonly WebApplication _app;

    public WebsitePageTests()
    {
        // Host the website in memory, the way the Windows and Linux hosts do, without the server.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Website:ServiceIndexUrl", ServiceIndexUrl },
        });

        builder.Services.AddNuDepotUI();
        builder.Services.AddSingleton(_search.Object);
        builder.Services.AddSingleton(_packages.Object);
        builder.Services.AddSingleton(_serviceIndex.Object);

        _app = builder.Build();
        _app.UseRouting();
        _app.MapNuDepotUI();
        _app.StartAsync().GetAwaiter().GetResult();

        _search
            .Setup(s => s.SearchAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponse { Data = new List<SearchResult>() });
    }

    public void Dispose() => _app.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [Fact]
    public async Task SearchPageListsPackages()
    {
        _search
            .Setup(s => s.SearchAsync("json", 0, 20, true, true, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponse
            {
                Data = new List<SearchResult>
                {
                    new SearchResult
                    {
                        PackageId = "Contoso.Json",
                        Version = "1.2.0",
                        Description = "Reads <json> & writes it.",
                        Authors = new[] { "Contoso" },
                        Tags = new[] { "json", "serialization" },
                        TotalDownloads = 1500,
                        Versions = new List<SearchResultVersion>(),
                    },
                },
            });

        var html = await GetPageAsync("/?q=json");

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

        _search.Verify(s => s.SearchAsync("json", 20, 20, false, true, "dotnettool", null, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task SearchPageRejectsInvalidPage()
    {
        using var client = _app.GetTestClient();

        var response = await client.GetAsync("/?p=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PackagePageShowsDetails()
    {
        _packages
            .Setup(p => p.GetPackageMetadataAsync("Contoso.Json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NuDepotPackageMetadata>
            {
                new NuDepotPackageMetadata
                {
                    PackageId = "Contoso.Json",
                    Version = "1.2.0",
                    Description = "Reads JSON.",
                    Authors = "Contoso",
                    Listed = true,
                    Published = DateTimeOffset.UtcNow,
                    PackageContentUrl = "https://feed.test/v3/package/contoso.json/1.2.0/contoso.json.1.2.0.nupkg",
                    Deprecation = new PackageDeprecation
                    {
                        Reasons = new[] { "Legacy" },
                        AlternatePackage = new AlternatePackage { Id = "Contoso.Text.Json", Range = "*" },
                    },
                },
            });
        _packages
            .Setup(p => p.GetPackageDependentsAsync("Contoso.Json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DependentsResponse { Data = new List<PackageDependent>() });

        var html = await GetPageAsync("/packages/Contoso.Json/1.2.0");

        Assert.Contains("<title>Contoso.Json 1.2.0 - NuDepot</title>", html);
        Assert.Contains("This package is no longer maintained.", html);
        Assert.Contains("href=\"/packages/Contoso.Text.Json\"", html);
        Assert.Contains("href=\"https://feed.test/v3/package/contoso.json/1.2.0/contoso.json.1.2.0.nupkg\"", html);
        Assert.Contains("<span>Versions</span>", html);

        // The install commands are passed to the page's script as JSON, which can't end the script early.
        Assert.Contains("\"dotnet add package Contoso.Json --version 1.2.0\"", html);
        Assert.Contains("\\u003CPackageReference Include=\\u0022Contoso.Json\\u0022", html);
    }

    [Fact]
    public async Task PackagePageShowsMissingPackage()
    {
        _packages
            .Setup(p => p.GetPackageMetadataAsync("Missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NuDepotPackageMetadata>());

        var html = await GetPageAsync("/packages/Missing");

        Assert.Contains("Could not find package 'Missing'.", html);
    }

    [Fact]
    public async Task UploadPageShowsPushCommands()
    {
        _serviceIndex
            .Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceIndexResponse
            {
                Version = "3.0.0",
                Resources = new List<ServiceIndexItem>
                {
                    new ServiceIndexItem { Type = "PackagePublish/2.0.0", ResourceUrl = "https://feed.test/api/v2/package" },
                },
            });

        var html = await GetPageAsync("/upload");

        Assert.Contains($"<code>{ServiceIndexUrl}</code>", html);
        Assert.Contains($"\"dotnet nuget push -s {ServiceIndexUrl} package.nupkg\"", html);
        Assert.Contains("class=\"active\"><span>Upload</span>", html);
    }

    [Fact]
    public async Task ExplainsWhenFeedIsUnreachable()
    {
        _search
            .Setup(s => s.SearchAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        using var client = _app.GetTestClient();
        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("the feed isn't responding", html);
        Assert.Contains($"<code>{ServiceIndexUrl}</code>", html);
    }

    [Theory]
    [InlineData("/_content/NuDepot.Gallery/css/site.css", "text/css")]
    [InlineData("/_content/NuDepot.Gallery/lib/bootstrap/dist/js/bootstrap.min.js", "text/javascript")]
    [InlineData("/_content/NuDepot.Gallery/images/github-32x32.png", "image/png")]
    public async Task ServesEmbeddedFiles(string url, string contentType)
    {
        using var client = _app.GetTestClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task EmbeddedFilesAreNotModifiedForTheSameETag()
    {
        using var client = _app.GetTestClient();
        var first = await client.GetAsync("/_content/NuDepot.Gallery/css/site.css");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/_content/NuDepot.Gallery/css/site.css");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var second = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Theory]
    [InlineData("/_content/NuDepot.Gallery/css/missing.css")]
    [InlineData("/_content/NuDepot.Gallery/css")]
    public async Task MissingEmbeddedFilesAreNotFound(string url)
    {
        using var client = _app.GetTestClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/account")]
    [InlineData("/account/signin")]
    public async Task AccountPagesNeedAFeedWithUsers(string url)
    {
        using var client = _app.GetTestClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoSignInLinkWithoutUsers()
    {
        var html = await GetPageAsync("/");

        Assert.DoesNotContain("account/signin", html);
        Assert.DoesNotContain("account-nav", html);
    }

    private async Task<string> GetPageAsync(string url)
    {
        using var client = _app.GetTestClient();
        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        return await response.Content.ReadAsStringAsync();
    }
}
