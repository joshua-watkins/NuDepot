using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Tests.Support;
using NuGet.Versioning;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace NuDepot.Feed.Tests;

/// <summary>
/// The files the feed serves from inside packages: their embedded icons, readmes and license files.
/// </summary>
public sealed class PackageFilesIntegrationTests(ITestOutputHelper output) : IDisposable
{
    private NuDepotTestApplication App { get; } = new(output, configuration: new Dictionary<string, string>
    {
        ["PackageDeletionBehavior"] = "HardDelete",
    });

    public void Dispose() => App.Dispose();

    [Fact]
    public async Task ServesEmbeddedFiles()
    {
        await App.AddPackageAsync(TestPackages.Create("Files.Package", "1.0.0", icon: "icon.png", readme: "README.md", licenseFile: "LICENSE.md"), cancellationToken: TestContext.Current.CancellationToken);
        using HttpClient client = App.CreateClient();

        await AssertFileAsync(client, "v3/package/files.package/1.0.0/icon", "icon.png", "image/png");
        await AssertFileAsync(client, "v3/package/files.package/1.0.0/readme", "README.md", null);
        await AssertFileAsync(client, "v3/package/files.package/1.0.0/license.md", "LICENSE.md", "text/markdown");
    }

    [Fact]
    public async Task MetadataLinksToEmbeddedFiles()
    {
        await App.AddPackageAsync(TestPackages.Create("Files.Package", "1.0.0", icon: "icon.png", readme: "README.md", licenseFile: "LICENSE.md"), cancellationToken: TestContext.Current.CancellationToken);
        using HttpClient client = App.CreateClient();

        JsonElement registration = await client.GetFromJsonAsync<JsonElement>("v3/registration/files.package/index.json", cancellationToken: TestContext.Current.CancellationToken);
        JsonElement entry = registration.GetProperty("items")[0].GetProperty("items")[0].GetProperty("catalogEntry");
        JsonElement result = (await client.GetFromJsonAsync<JsonElement>("v3/search?q=Files.Package", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("data")[0];
        JsonElement catalog = await client.GetFromJsonAsync<JsonElement>("v3/catalog/index.json", cancellationToken: TestContext.Current.CancellationToken);
        JsonElement page = await client.GetFromJsonAsync<JsonElement>(catalog.GetProperty("items")[0].GetProperty("@id").GetString(), cancellationToken: TestContext.Current.CancellationToken);
        JsonElement leaf = await client.GetFromJsonAsync<JsonElement>(page.GetProperty("items")[0].GetProperty("@id").GetString(), cancellationToken: TestContext.Current.CancellationToken);

        foreach ((JsonElement metadata, string property, string file) in (List<(JsonElement, string, string)>)
        [
            (entry, "iconUrl", "icon.png"),
            (entry, "licenseUrl", "LICENSE.md"),
            (entry, "readmeUrl", "README.md"),
            (result, "iconUrl", "icon.png"),
            (result, "licenseUrl", "LICENSE.md"),
            (leaf, "iconUrl", "icon.png"),
            (leaf, "licenseUrl", "LICENSE.md"),
        ])
        {
            string url = metadata.GetProperty(property).GetString()!;
            Assert.StartsWith("http://localhost/v3/package/files.package/1.0.0/", url);
            await AssertFileAsync(client, url, file, null);
        }
    }

    [Fact]
    public async Task ServesTextLicenses()
    {
        await App.AddPackageAsync(TestPackages.Create("Text.License", "1.0.0", licenseFile: "LICENSE.txt"), cancellationToken: TestContext.Current.CancellationToken);
        using HttpClient client = App.CreateClient();

        await AssertFileAsync(client, "v3/package/text.license/1.0.0/license.txt", "LICENSE.txt", "text/plain");
    }

    [Theory]
    [InlineData("v3/package/plain.package/1.0.0/icon")]
    [InlineData("v3/package/plain.package/1.0.0/readme")]
    [InlineData("v3/package/plain.package/1.0.0/license.txt")]
    [InlineData("v3/package/plain.package/1.0.0/license.md")]
    [InlineData("v3/package/missing.package/1.0.0/icon")]
    [InlineData("v3/package/missing.package/1.0.0/license.txt")]
    public async Task FilesAPackageDoesntHaveAreNotFound(string url)
    {
        await App.AddPackageAsync(TestPackages.Create("Plain.Package", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        using HttpClient client = App.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task DeletingAPackageDeletesItsFiles()
    {
        await App.AddPackageAsync(TestPackages.Create("Files.Package", "1.0.0", icon: "icon.png", readme: "README.md", licenseFile: "LICENSE.txt"), cancellationToken: TestContext.Current.CancellationToken);
        using HttpClient client = App.CreateClient();

        using (IServiceScope scope = App.Services.CreateScope())
        {
            Assert.True(await scope.ServiceProvider
                .GetRequiredService<IPackageDeletionService>()
                .TryDeletePackageAsync("Files.Package", NuGetVersion.Parse("1.0.0"), CancellationToken.None));
        }

        string storage = App.Services.GetRequiredService<IOptions<FeedOptions>>().Value.Storage.Path!;
        string folder = Path.Combine(storage, "packages", "files.package", "1.0.0");
        Assert.Empty(Directory.Exists(folder) ? Directory.GetFiles(folder) : []);

        // So it can be pushed again.
        await App.AddPackageAsync(TestPackages.Create("Files.Package", "1.0.0", icon: "icon.png"), cancellationToken: TestContext.Current.CancellationToken);
        await AssertFileAsync(client, "v3/package/files.package/1.0.0/icon", "icon.png", "image/png");
    }

    private static async Task AssertFileAsync(HttpClient client, string url, string file, string? contentType)
    {
        using HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestPackages.FileContent(file), await response.Content.ReadAsByteArrayAsync());
        if (contentType != null)
            Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
    }
}
