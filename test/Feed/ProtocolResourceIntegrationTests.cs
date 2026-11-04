using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NuDepot.Core.Catalog;
using NuDepot.Core.Models;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Tests.Support;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Tests the NuGet protocol resources beyond the basics: search, SemVer 2.0.0 registrations, the catalog,
/// deprecations, vulnerabilities, license expressions, the package details template and repository signing.
/// </summary>
public class ProtocolResourceIntegrationTests(ITestOutputHelper output) : IDisposable
{
    private ITestOutputHelper Output { get; } = output;
    private List<IDisposable> Disposables { get; } = [];

    public void Dispose()
    {
        foreach (IDisposable disposable in Disposables)
            disposable.Dispose();
    }

    [Fact]
    public async Task SearchTotalHitsCountsAllMatches()
    {
        HttpClient client = CreateClient(out _);
        await PushAsync(client, TestPackages.Create("Paging.A", "1.0.0"));
        await PushAsync(client, TestPackages.Create("Paging.B", "1.0.0"));
        await PushAsync(client, TestPackages.Create("Paging.C", "1.0.0"));

        JsonElement search = await GetJsonAsync(client, "v3/search?take=1");

        Assert.Equal(3, search.GetProperty("totalHits").GetInt32());
        Assert.Equal(1, search.GetProperty("data").GetArrayLength());

        JsonElement autocomplete = await GetJsonAsync(client, "v3/autocomplete?q=paging&take=2");

        Assert.Equal(3, autocomplete.GetProperty("totalHits").GetInt32());
        Assert.Equal(2, autocomplete.GetProperty("data").GetArrayLength());
    }

    [Theory]
    [InlineData("api/v2")]
    [InlineData("api/v2/")]
    [InlineData("api/v2/$metadata")]
    [InlineData("api/v2/FindPackagesById()?id='Contoso.Json'")]
    [InlineData("api/v2/package/Contoso.Json/1.0.0")]
    public async Task V2FeedIsNotSupported(string path)
    {
        HttpClient client = CreateClient(out _);
        await PushAsync(client, TestPackages.Create("Contoso.Json", "1.0.0"));

        HttpResponseMessage response = await client.GetAsync(path);
        string message = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("V2 (OData) feed is not supported", message);
        Assert.Contains("http://localhost/v3/index.json", message);
    }

    [Theory]
    [InlineData("serializer", new[] { "Contoso.Json" })]
    [InlineData("json", new[] { "Contoso.Json", "Helpers" })]
    [InlineData("tags:serialization", new[] { "Other.Package" })]
    [InlineData("packageid:contoso.json", new[] { "Contoso.Json" })]
    [InlineData("author:\"Jane Doe\"", new[] { "Helpers" })]
    [InlineData("id:other", new[] { "Other.Package" })]
    [InlineData("version:2.0.0", new[] { "Helpers" })]
    [InlineData("json author:\"Jane Doe\"", new[] { "Helpers" })]
    [InlineData("nothing-matches", new string[0])]
    public async Task SearchMatchesMetadataAndFieldFilters(string query, string[] expectedIds)
    {
        HttpClient client = CreateClient(out _);
        await PushAsync(client, TestPackages.Create("Contoso.Json", "1.0.0", description: "A fast serializer"));
        await PushAsync(client, TestPackages.Create("Other.Package", "1.0.0", tags: ["serialization"]));
        await PushAsync(client, TestPackages.Create("Helpers", "2.0.0", title: "Json helpers", author: "Jane Doe"));

        JsonElement search = await GetJsonAsync(client, "v3/search?q=" + Uri.EscapeDataString(query));
        string[] ids = [.. search.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetString()!)];

        // Results are ranked: an ID match outranks a title match.
        Assert.Equal(expectedIds, ids);
        Assert.Equal(expectedIds.Length, search.GetProperty("totalHits").GetInt32());
    }

    [Fact]
    public async Task SearchReturnsAndFiltersPackageTypes()
    {
        HttpClient client = CreateClient(out _);
        await PushAsync(client, TestPackages.Create("Contoso.Templates", "1.0.0", packageType: "Template"));
        await PushAsync(client, TestPackages.Create("Contoso.Library", "1.0.0"));

        JsonElement search = await GetJsonAsync(client, "v3/search?packageType=template");

        JsonElement result = Assert.Single(search.GetProperty("data").EnumerateArray());
        Assert.Equal("Contoso.Templates", result.GetProperty("id").GetString()!);
        Assert.Equal("Template", result.GetProperty("packageTypes")[0].GetProperty("name").GetString()!);
    }

    [Fact]
    public async Task SemVer2PackagesOnlyAppearInTheSemVer2Registration()
    {
        HttpClient client = CreateClient(out _);
        await PushAsync(client, TestPackages.Create("SemVer.Package", "1.0.0"));
        await PushAsync(client, TestPackages.Create("SemVer.Package", "2.0.0-beta.1"));

        JsonElement semVer1 = await GetJsonAsync(client, "v3/registration/semver.package/index.json");
        IEnumerable<string> semVer1Versions = semVer1.GetProperty("items")[0]
            .GetProperty("items")
            .EnumerateArray()
            .Select(i => i.GetProperty("catalogEntry").GetProperty("version").GetString()!);
        Assert.Equal(["1.0.0"], semVer1Versions);

        JsonElement semVer2 = await GetJsonAsync(client, "v3/registration-semver2/semver.package/index.json");
        IEnumerable<string> semVer2Versions = semVer2.GetProperty("items")[0]
            .GetProperty("items")
            .EnumerateArray()
            .Select(i => i.GetProperty("catalogEntry").GetProperty("version").GetString()!);
        Assert.Equal(["1.0.0", "2.0.0-beta.1"], semVer2Versions);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("v3/registration/semver.package/2.0.0-beta.1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("v3/registration-semver2/semver.package/2.0.0-beta.1.json")).StatusCode);

        JsonElement semVer1Search = await GetJsonAsync(client, "v3/search?q=semver&prerelease=true");
        Assert.Equal("1.0.0", semVer1Search.GetProperty("data")[0].GetProperty("version").GetString()!);

        JsonElement semVer2Search = await GetJsonAsync(client, "v3/search?q=semver&prerelease=true&semVerLevel=2.0.0");
        JsonElement result = semVer2Search.GetProperty("data")[0];
        Assert.Equal("2.0.0-beta.1", result.GetProperty("version").GetString()!);
        Assert.Contains("/v3/registration-semver2/", result.GetProperty("registration").GetString()!);
    }

    [Fact]
    public async Task CatalogRecordsPushUnlistAndRelist()
    {
        // The server's appsettings.json hard deletes by default.
        HttpClient client = CreateClient(out _, new Dictionary<string, string> { { "PackageDeletionBehavior", "Unlist" } });
        await PushAsync(client, TestPackages.Create("Catalog.Package", "1.0.0"));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("api/v2/package/Catalog.Package/1.0.0")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("api/v2/package/Catalog.Package/1.0.0", null)).StatusCode);

        JsonElement index = await GetJsonAsync(client, "v3/catalog/index.json");
        Assert.Equal(1, index.GetProperty("count").GetInt32());

        string pageUrl = index.GetProperty("items")[0]
            .GetProperty("@id")
            .GetString()!;
        JsonElement page = await GetJsonAsync(client, pageUrl);
        List<JsonElement> items = [.. page.GetProperty("items").EnumerateArray()];

        Assert.Equal(3, page.GetProperty("count").GetInt32());
        Assert.All(items, i => Assert.Equal("nuget:PackageDetails", i.GetProperty("@type").GetString()!));
        Assert.All(items, i => Assert.Equal("Catalog.Package", i.GetProperty("nuget:id").GetString()!));

        // Every commit has a later timestamp than the one before it.
        List<DateTimeOffset> timestamps = [.. items.Select(i => i.GetProperty("commitTimeStamp").GetDateTimeOffset())];
        Assert.Equal(timestamps.OrderBy(t => t).Distinct(), timestamps);
        Assert.Equal(index.GetProperty("commitId").GetString(), items[2].GetProperty("commitId").GetString()!);

        List<bool> listed = [];
        foreach (JsonElement item in items)
        {
            JsonElement leaf = await GetJsonAsync(client, item.GetProperty("@id").GetString()!);
            listed.Add(leaf.GetProperty("listed").GetBoolean());
        }

        Assert.Equal(new[] { true, false, true }, listed);
    }

    [Fact]
    public async Task CatalogLeafDescribesTheStoredPackage()
    {
        HttpClient client = CreateClient(out _);
        await PushAsync(client, TestPackages.Create("Catalog.Package", "1.0.0", licenseExpression: "MIT", packageType: "Template"));

        JsonElement leaf = await GetLatestCatalogLeafAsync(client);
        byte[] package = await client.GetByteArrayAsync("v3/package/catalog.package/1.0.0/catalog.package.1.0.0.nupkg");

        Assert.Contains("PackageDetails", leaf.GetProperty("@type").EnumerateArray().Select(t => t.GetString()!));
        Assert.Equal("Catalog.Package", leaf.GetProperty("id").GetString()!);
        Assert.Equal("1.0.0", leaf.GetProperty("version").GetString()!);
        Assert.Equal("SHA512", leaf.GetProperty("packageHashAlgorithm").GetString()!);
        Assert.Equal(Convert.ToBase64String(SHA512.HashData(package)), leaf.GetProperty("packageHash").GetString()!);
        Assert.Equal(package.Length, leaf.GetProperty("packageSize").GetInt64());
        Assert.Equal("MIT", leaf.GetProperty("licenseExpression").GetString()!);
        Assert.Equal("Template", leaf.GetProperty("packageTypes")[0].GetProperty("name").GetString()!);

        JsonElement registration = await GetCatalogEntryAsync(client, "catalog.package");
        Assert.Equal("MIT", registration.GetProperty("licenseExpression").GetString()!);
        Assert.Equal(leaf.GetProperty("@id").GetString(), registration.GetProperty("@id").GetString()!);
    }

    [Fact]
    public async Task CatalogRecordsHardDeletes()
    {
        HttpClient client = CreateClient(out _, new Dictionary<string, string> { { "PackageDeletionBehavior", "HardDelete" } });
        await PushAsync(client, TestPackages.Create("Catalog.Package", "1.0.0"));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("api/v2/package/Catalog.Package/1.0.0")).StatusCode);

        JsonElement leaf = await GetLatestCatalogLeafAsync(client);

        Assert.Contains("PackageDelete", leaf.GetProperty("@type").EnumerateArray().Select(t => t.GetString()!));
        Assert.Equal("Catalog.Package", leaf.GetProperty("id").GetString()!);
        Assert.Equal("1.0.0", leaf.GetProperty("version").GetString()!);
    }

    [Fact]
    public async Task CatalogIsReadableByTheCatalogClient()
    {
        HttpClient client = CreateClient(out _);
        await PushAsync(client, TestPackages.Create("Catalog.Package", "1.0.0"));

        RawCatalogClient catalogClient = new(client, new Uri(client.BaseAddress!, "v3/catalog/index.json").AbsoluteUri);

        CatalogIndex index = await catalogClient.GetIndexAsync();
        CatalogPage page = await catalogClient.GetPageAsync(index.Items[0].CatalogPageUrl);
        PackageDetailsCatalogLeaf leaf = await catalogClient.GetPackageDetailsLeafAsync(page.Items[0].CatalogLeafUrl);

        Assert.Equal("Catalog.Package", leaf.PackageId);
        Assert.Equal("1.0.0", leaf.PackageVersion);
        Assert.True(leaf.Listed);
    }

    [Fact]
    public async Task CatalogIncludesPackagesFromBeforeItExisted()
    {
        HttpClient client = CreateClient(out NuDepotTestApplication? app);
        await PushAsync(client, TestPackages.Create("Old.Package", "1.0.0"));

        // Make the package look like it was indexed before the catalog existed.
        using (IServiceScope scope = app.Services.CreateScope())
        {
            NuDepotContext context = scope.ServiceProvider.GetRequiredService<NuDepotContext>();
            context.CatalogEntries.RemoveRange(await context.CatalogEntries.ToListAsync());
            foreach (Package? existing in await context.Packages.ToListAsync())
                existing.PackageHash = null;
            await context.SaveChangesAsync(default);

            await scope.ServiceProvider
                .GetRequiredService<ICatalogService>()
                .InitializeAsync(default);
        }

        JsonElement leaf = await GetLatestCatalogLeafAsync(client);
        byte[] package = await client.GetByteArrayAsync("v3/package/old.package/1.0.0/old.package.1.0.0.nupkg");

        Assert.Equal("Old.Package", leaf.GetProperty("id").GetString()!);
        Assert.Equal(Convert.ToBase64String(SHA512.HashData(package)), leaf.GetProperty("packageHash").GetString()!);
    }

    [Fact]
    public async Task DeprecationIsPublished()
    {
        HttpClient client = CreateClient(out _);
        await PushAsync(client, TestPackages.Create("Old.Package", "1.0.0"));

        HttpResponseMessage response = await client.PutAsJsonAsync("api/package/Old.Package/1.0.0/deprecation", new
        {
            reasons = new[] { "legacy" },
            message = "Use New.Package instead.",
            alternatePackageId = "New.Package",
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        JsonElement deprecation = (await GetCatalogEntryAsync(client, "old.package")).GetProperty("deprecation");
        Assert.Equal("Legacy", deprecation.GetProperty("reasons")[0].GetString()!);
        Assert.Equal("Use New.Package instead.", deprecation.GetProperty("message").GetString()!);
        Assert.Equal("New.Package", deprecation.GetProperty("alternatePackage").GetProperty("id").GetString()!);
        Assert.Equal("*", deprecation.GetProperty("alternatePackage").GetProperty("range").GetString()!);

        JsonElement search = await GetJsonAsync(client, "v3/search?q=old.package");
        Assert.Equal("Legacy", search.GetProperty("data")[0].GetProperty("deprecation").GetProperty("reasons")[0].GetString()!);

        JsonElement leaf = await GetLatestCatalogLeafAsync(client);
        Assert.Equal("Legacy", leaf.GetProperty("deprecation").GetProperty("reasons")[0].GetString()!);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("api/package/Old.Package/1.0.0/deprecation")).StatusCode);
        Assert.False((await GetCatalogEntryAsync(client, "old.package")).TryGetProperty("deprecation", out _));
    }

    [Theory]
    [InlineData("Old.Package", "{\"reasons\":[\"Unknown\"]}", HttpStatusCode.BadRequest)]
    [InlineData("Old.Package", "{\"reasons\":[]}", HttpStatusCode.BadRequest)]
    [InlineData("Old.Package", "{\"reasons\":[\"Other\"],\"alternatePackageId\":\"X\",\"alternatePackageVersionRange\":\"not a range\"}", HttpStatusCode.BadRequest)]
    [InlineData("Missing.Package", "{\"reasons\":[\"Other\"]}", HttpStatusCode.NotFound)]
    public async Task DeprecationValidatesRequests(string packageId, string body, HttpStatusCode expected)
    {
        HttpClient client = CreateClient(out _);
        await PushAsync(client, TestPackages.Create("Old.Package", "1.0.0"));

        using StringContent content = new(body, System.Text.Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PutAsync($"api/package/{packageId}/1.0.0/deprecation", content);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task VulnerabilitiesArePublished()
    {
        HttpClient client = CreateClient(out _);
        await PushAsync(client, TestPackages.Create("Vulnerable.Package", "1.0.0"));
        await PushAsync(client, TestPackages.Create("Vulnerable.Package", "2.0.0"));

        // Without vulnerability data, the resource must not be advertised.
        Assert.DoesNotContain("VulnerabilityInfo/6.7.0", await GetResourceTypesAsync(client));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("v3/vulnerabilities/index.json")).StatusCode);

        HttpResponseMessage response = await client.PutAsJsonAsync("api/vulnerabilities", new
        {
            packageId = "Vulnerable.Package",
            versions = "(, 2.0.0)",
            url = "https://example.com/advisories/1",
            severity = 2,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Contains("VulnerabilityInfo/6.7.0", await GetResourceTypesAsync(client));

        JsonElement index = await GetJsonAsync(client, "v3/vulnerabilities/index.json");
        JsonElement file = Assert.Single(index.EnumerateArray());
        Assert.Equal("base", file.GetProperty("@name").GetString()!);

        JsonElement vulnerabilities = await GetJsonAsync(client, file.GetProperty("@id").GetString()!);
        JsonElement vulnerability = Assert.Single(vulnerabilities.GetProperty("vulnerable.package").EnumerateArray());
        Assert.Equal(2, vulnerability.GetProperty("severity").GetInt32());
        Assert.Equal("https://example.com/advisories/1", vulnerability.GetProperty("url").GetString()!);
        Assert.Equal("(, 2.0.0)", vulnerability.GetProperty("versions").GetString()!);

        // Only the affected version reports the vulnerability.
        JsonElement registration = await GetJsonAsync(client, "v3/registration/vulnerable.package/index.json");
        Dictionary<string, JsonElement> entries = registration.GetProperty("items")[0]
            .GetProperty("items")
            .EnumerateArray()
            .Select(i => i.GetProperty("catalogEntry"))
            .ToDictionary(e => e.GetProperty("version").GetString()!);

        JsonElement reported = Assert.Single(entries["1.0.0"].GetProperty("vulnerabilities").EnumerateArray());
        Assert.Equal("https://example.com/advisories/1", reported.GetProperty("advisoryUrl").GetString()!);
        Assert.Equal("2", reported.GetProperty("severity").GetString()!);
        Assert.False(entries["2.0.0"].TryGetProperty("vulnerabilities", out _));

        // The affected version gets a new catalog entry.
        JsonElement leaf = await GetLatestCatalogLeafAsync(client);
        Assert.Equal("1.0.0", leaf.GetProperty("version").GetString()!);
        Assert.Equal("2", leaf.GetProperty("vulnerabilities")[0].GetProperty("severity").GetString()!);

        HttpResponseMessage delete = await client.DeleteAsync("api/vulnerabilities?packageId=Vulnerable.Package&url=" + Uri.EscapeDataString("https://example.com/advisories/1"));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.DoesNotContain("VulnerabilityInfo/6.7.0", await GetResourceTypesAsync(client));
    }

    [Fact]
    public async Task PackageDetailsUriTemplateIsOnlyAdvertisedOverHttps()
    {
        Dictionary<string, string> configuration = new()
        { { "PackageDetailsUriTemplate", "packages/{id}/{version}" } };
        NuDepotTestApplication app = CreateApp(configuration);

        JsonElement https = await GetJsonAsync(app.CreateHttpsClient(), "v3/index.json");
        JsonElement template = https
            .GetProperty("resources")
            .EnumerateArray()
            .Single(r => r.GetProperty("@type").GetString() == "PackageDetailsUriTemplate/5.1.0");
        Assert.Equal("https://localhost/packages/{id}/{version}", template.GetProperty("@id").GetString()!);

        Assert.DoesNotContain("PackageDetailsUriTemplate/5.1.0", await GetResourceTypesAsync(app.CreateClient()));
    }

    [Fact]
    public async Task PushedPackagesAreRepositorySigned()
    {
        string pfxPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pfx");
        using X509Certificate2 certificate = TestCertificates.CreateCodeSigningPfx(pfxPath, "password");
        string fingerprint = Convert.ToHexStringLower(SHA256.HashData(certificate.RawData));

        try
        {
            NuDepotTestApplication app = CreateApp(new Dictionary<string, string>
            {
                { "RepositorySigning:CertificatePath", pfxPath },
                { "RepositorySigning:CertificatePassword", "password" },
            });
            HttpClient client = app.CreateHttpsClient();

            IReadOnlyList<string> resourceTypes = await GetResourceTypesAsync(client);
            Assert.Contains("RepositorySignatures/4.7.0", resourceTypes);
            Assert.Contains("RepositorySignatures/4.9.0", resourceTypes);
            Assert.Contains("RepositorySignatures/5.0.0", resourceTypes);
            Assert.DoesNotContain("RepositorySignatures/5.0.0", await GetResourceTypesAsync(app.CreateClient()));

            Assert.Equal(HttpStatusCode.Created, (await PushAsync(client, TestPackages.Create("Signed.Package", "1.0.0"))).StatusCode);

            byte[] package = await client.GetByteArrayAsync("v3/package/signed.package/1.0.0/signed.package.1.0.0.nupkg");
            using (PackageArchiveReader reader = new(new MemoryStream(package)))
            {
                Assert.True(await reader.IsSignedAsync(default));

                RepositoryPrimarySignature signature = Assert.IsType<RepositoryPrimarySignature>(await reader.GetPrimarySignatureAsync(default));
                Assert.Equal(new Uri("https://localhost/v3/index.json"), signature.V3ServiceIndexUrl);
                Assert.Equal(certificate.Thumbprint, signature.SignerInfo.Certificate!.Thumbprint);
            }

            // The catalog describes the signed package.
            JsonElement leaf = await GetLatestCatalogLeafAsync(client);
            Assert.Equal(Convert.ToBase64String(SHA512.HashData(package)), leaf.GetProperty("packageHash").GetString()!);

            JsonElement legacy = await GetJsonAsync(client, "v3/repository-signatures/4.7.0/index.json");
            Assert.False(legacy.GetProperty("allRepositorySigned").GetBoolean());

            JsonElement current = await GetJsonAsync(client, "v3/repository-signatures/5.0.0/index.json");
            Assert.True(current.GetProperty("allRepositorySigned").GetBoolean());

            JsonElement signingCertificate = Assert.Single(current.GetProperty("signingCertificates").EnumerateArray());
            Assert.Equal(fingerprint, signingCertificate.GetProperty("fingerprints").GetProperty("2.16.840.1.101.3.4.2.1").GetString()!);
            Assert.Equal(certificate.Subject, signingCertificate.GetProperty("subject").GetString()!);

            byte[] download = await client.GetByteArrayAsync(signingCertificate.GetProperty("contentUrl").GetString()!);
            Assert.Equal(certificate.RawData, download);

            // Pushing over HTTP can't record an HTTPS service index URL, so the package isn't signed.
            await PushAsync(app.CreateClient(), TestPackages.Create("Unsigned.Package", "1.0.0"));
            JsonElement afterUnsigned = await GetJsonAsync(client, "v3/repository-signatures/5.0.0/index.json");
            Assert.False(afterUnsigned.GetProperty("allRepositorySigned").GetBoolean());
        }
        finally
        {
            File.Delete(pfxPath);
        }
    }

    [Fact]
    public async Task RepositorySignaturesAreNotAdvertisedWithoutACertificate()
    {
        HttpClient client = CreateApp().CreateHttpsClient();

        Assert.DoesNotContain(await GetResourceTypesAsync(client), t => t.StartsWith("RepositorySignatures/"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("v3/repository-signatures/5.0.0/index.json")).StatusCode);
    }

    private NuDepotTestApplication CreateApp(IReadOnlyDictionary<string, string>? configuration = null)
    {
        NuDepotTestApplication app = new(Output, configuration: configuration);
        Disposables.Add(app);
        return app;
    }

    private HttpClient CreateClient(out NuDepotTestApplication app, IReadOnlyDictionary<string, string>? configuration = null)
    {
        app = CreateApp(configuration);
        return app.CreateClient();
    }

    private static async Task<HttpResponseMessage> PushAsync(HttpClient client, Stream package)
    {
        using MultipartFormDataContent content = new()
        {
            { new StreamContent(package), "package", "package.nupkg" },
        };

        HttpResponseMessage response = await client.PutAsync("api/v2/package", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return response;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        using HttpResponseMessage response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<IReadOnlyList<string>> GetResourceTypesAsync(HttpClient client)
    {
        JsonElement index = await GetJsonAsync(client, "v3/index.json");

        return [.. index.GetProperty("resources").EnumerateArray().Select(r => r.GetProperty("@type").GetString()!)];
    }

    private static async Task<JsonElement> GetCatalogEntryAsync(HttpClient client, string id)
    {
        JsonElement registration = await GetJsonAsync(client, $"v3/registration/{id}/index.json");

        return registration.GetProperty("items")[0].GetProperty("items")[0].GetProperty("catalogEntry");
    }

    private static async Task<JsonElement> GetLatestCatalogLeafAsync(HttpClient client)
    {
        JsonElement index = await GetJsonAsync(client, "v3/catalog/index.json");
        JsonElement latestPage = index
            .GetProperty("items")
            .EnumerateArray()
            .MaxBy(p => p.GetProperty("commitTimeStamp").GetDateTimeOffset());

        JsonElement page = await GetJsonAsync(client, latestPage.GetProperty("@id").GetString()!);
        JsonElement latestItem = page
            .GetProperty("items")
            .EnumerateArray()
            .MaxBy(i => i.GetProperty("commitTimeStamp").GetDateTimeOffset());

        return await GetJsonAsync(client, latestItem.GetProperty("@id").GetString()!);
    }
}
