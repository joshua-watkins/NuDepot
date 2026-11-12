using Microsoft.Extensions.DependencyInjection;
using Moq;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Tests.Support;
using System.IO.Compression;
using System.Net;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Pushing, unlisting and relisting packages and symbols, as NuGet clients do. The feed has no API keys, so anyone
/// may push and unlist.
/// </summary>
public class PackagePublishIntegrationTests(ITestOutputHelper output) : IDisposable
{
    private NuDepotTestApplication App { get; } = new(output, configuration: new Dictionary<string, string>
    {
        ["PackageDeletionBehavior"] = "Unlist",
    });

    public void Dispose() => App.Dispose();

    [Fact]
    public async Task PushesPackage()
    {
        using HttpClient client = App.CreateClient();

        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "api/v2/package", Resource(TestResources.Package)));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("v3/package/testdata/1.2.3/testdata.1.2.3.nupkg")).StatusCode);
    }

    [Fact]
    public async Task PushingAVersionTwiceConflicts()
    {
        using HttpClient client = App.CreateClient();

        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "api/v2/package", Resource(TestResources.Package)));
        Assert.Equal(HttpStatusCode.Conflict, await PushAsync(client, "api/v2/package", Resource(TestResources.Package)));
    }

    [Fact]
    public async Task ConcurrentPushesOfAVersionConflictInsteadOfFailing()
    {
        using HttpClient client = App.CreateClient();
        byte[] package = Resource(TestResources.Package);

        HttpStatusCode[] results = await Task.WhenAll(Enumerable
            .Range(0, 4)
            .Select(_ => PushAsync(client, "api/v2/package", package)));

        Assert.Equal(1, results.Count(r => r == HttpStatusCode.Created));
        Assert.Equal(3, results.Count(r => r == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task PushWithoutAPackageIsBad()
    {
        using HttpClient client = App.CreateClient();

        using HttpResponseMessage response = await client.PutAsync("api/v2/package", new StringContent(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("api/v2/package")]
    [InlineData("api/v2/symbol")]
    public async Task PushingSomethingElseIsBad(string url)
    {
        using HttpClient client = App.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, await PushAsync(client, url, Encoding.UTF8.GetBytes("not a package")));
    }

    [Fact]
    public async Task PushingAPackageThatWouldExtractOutsideItsFolderIsBad()
    {
        using HttpClient client = App.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, await PushAsync(client, "api/v2/package", UnsafePackage()));
    }

    [Fact]
    public async Task SymbolsNeedTheirPackage()
    {
        using HttpClient client = App.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, await PushAsync(client, "api/v2/symbol", Resource(TestResources.SymbolPackage)));

        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "api/v2/package", Resource(TestResources.Package)));
        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "api/v2/symbol", Resource(TestResources.SymbolPackage)));
    }

    [Theory]
    [InlineData("DELETE", "api/v2/package/TestData/not-a-version")]
    [InlineData("POST", "api/v2/package/TestData/not-a-version")]
    [InlineData("DELETE", "api/v2/package/Missing/1.0.0")]
    [InlineData("POST", "api/v2/package/Missing/1.0.0")]
    public async Task ChangingAMissingPackageIsNotFound(string method, string url)
    {
        using HttpClient client = App.CreateClient();

        using HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnlistsAndRelists()
    {
        using HttpClient client = App.CreateClient();
        await PushAsync(client, "api/v2/package", Resource(TestResources.Package));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("api/v2/package/TestData/1.2.3")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("api/v2/package/TestData/1.2.3", null)).StatusCode);
    }

    [Theory]
    [InlineData("api/v2/package")]
    [InlineData("api/v2/symbol")]
    public async Task StorageFailuresAreServerErrors(string url)
    {
        // Like a full disk.
        Mock<IPackageIndexingService> packages = new();
        packages
            .Setup(i => i.IndexAsync(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("There is not enough space on the disk."));
        Mock<ISymbolIndexingService> symbols = new();
        symbols
            .Setup(i => i.IndexAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("There is not enough space on the disk."));
        using NuDepotTestApplication app = new(output, configureServices: services => services
            .AddSingleton(packages.Object)
            .AddSingleton(symbols.Object));
        using HttpClient client = app.CreateClient();

        Assert.Equal(HttpStatusCode.InternalServerError, await PushAsync(client, url, Resource(TestResources.Package)));
    }

    [Fact]
    public async Task ADifferentPdbWithTheSameKeyConflicts()
    {
        Mock<ISymbolIndexingService> symbols = new();
        symbols
            .Setup(i => i.IndexAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SymbolIndexingResult.SymbolsConflict);
        using NuDepotTestApplication app = new(output, configureServices: services => services.AddSingleton(symbols.Object));
        using HttpClient client = app.CreateClient();

        Assert.Equal(HttpStatusCode.Conflict, await PushAsync(client, "api/v2/symbol", Resource(TestResources.SymbolPackage)));
    }

    [Fact]
    public async Task PushingTheSameSymbolsAgainSucceeds()
    {
        using HttpClient client = App.CreateClient();
        await PushAsync(client, "api/v2/package", Resource(TestResources.Package));

        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "api/v2/symbol", Resource(TestResources.SymbolPackage)));
        Assert.Equal(HttpStatusCode.Created, await PushAsync(client, "api/v2/symbol", Resource(TestResources.SymbolPackage)));
    }

    private static async Task<HttpStatusCode> PushAsync(HttpClient client, string url, byte[] package)
    {
        using MultipartFormDataContent content = new()
        {
            { new ByteArrayContent(package), "package", "package.nupkg" },
        };

        using HttpResponseMessage response = await client.PutAsync(url, content);
        return response.StatusCode;
    }

    private static byte[] Resource(string name) => TestResources.GetResourceStream(name).ToArray();

    // A package with an entry that would be extracted outside the package's folder.
    private static byte[] UnsafePackage()
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (StreamWriter nuspec = new(archive.CreateEntry("Unsafe.nuspec").Open()))
            {
                nuspec.Write("""
                    <?xml version="1.0" encoding="utf-8"?>
                    <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                      <metadata><id>Unsafe</id><version>1.0.0</version><authors>Contoso</authors><description>Unsafe.</description></metadata>
                    </package>
                    """);
            }

            using Stream entry = archive.CreateEntry("../../evil.dll").Open();
            entry.Write([1, 2, 3]);
        }

        return stream.ToArray();
    }
}
