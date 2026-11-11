using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Tests.Support;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Tests the gallery hosted with the feed and its users, the way the Windows and Linux services host them: what each
/// visitor may manage, and that nobody else sees it. The gallery calls the feed over the server's address, so the
/// application listens on a real port.
/// </summary>
public class GalleryManagementTests(ITestOutputHelper output) : IAsyncDisposable
{
    private ITestOutputHelper Output { get; } = output;
    private List<WebApplication> Apps { get; } = [];
    private List<IDisposable> Disposables { get; } = [];

    public async ValueTask DisposeAsync()
    {
        foreach (IDisposable disposable in Disposables)
            disposable.Dispose();
        foreach (WebApplication app in Apps)
            await app.DisposeAsync();
    }

    [Fact]
    public async Task AnonymousVisitorsDontSeeManagement()
    {
        (WebApplication app, Uri address) = await StartAsync();
        await AddPackageAsync(app, "Contoso.Utils", "1.0.0");
        HttpClient visitor = CreateBrowser(address);

        string package = await GetPageAsync(visitor, "packages/Contoso.Utils");
        string upload = await GetPageAsync(visitor, "upload");
        using HttpResponseMessage admin = await visitor.GetAsync("admin");

        Assert.DoesNotContain("manage-package", package);
        Assert.DoesNotContain("upload-form", upload);
        Assert.DoesNotContain("/admin\"", package);
        Assert.Equal(HttpStatusCode.Redirect, admin.StatusCode);
        Assert.Contains("account/signin", admin.Headers.Location!.ToString());
    }

    [Fact]
    public async Task ReadersDontSeeManagement()
    {
        (WebApplication app, Uri address) = await StartAsync();
        await AddPackageAsync(app, "Contoso.Utils", "1.0.0");
        await SignUpAsync(address, "alice");
        HttpClient bob = await SignUpAsync(address, "bob");

        string package = await GetPageAsync(bob, "packages/Contoso.Utils");
        string upload = await GetPageAsync(bob, "upload");
        using HttpResponseMessage admin = await bob.GetAsync("admin");

        Assert.DoesNotContain("manage-package", package);
        Assert.DoesNotContain("upload-form", upload);
        Assert.DoesNotContain("/admin\"", package);
        Assert.Equal(HttpStatusCode.NotFound, admin.StatusCode);
    }

    [Fact]
    public async Task AdminsManagePackages()
    {
        (WebApplication app, Uri address) = await StartAsync(new() { ["Feed:PackageDeletionBehavior"] = "Unlist" });
        await AddPackageAsync(app, "Contoso.Utils", "1.0.0");
        await AddPackageAsync(app, "Contoso.Utils", "2.0.0");
        HttpClient alice = await SignUpAsync(address, "alice");
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync("api/v2/package/Contoso.Utils/1.0.0")).StatusCode);

        string package = await GetPageAsync(alice, "packages/Contoso.Utils/2.0.0");

        Assert.Contains("id=\"manage-package\"", package);
        Assert.Contains("data-manage-method=\"DELETE\" data-manage-url=\"/api/v2/package/Contoso.Utils/2.0.0\">Unlist", package);
        Assert.Contains("data-manage-method=\"POST\" data-manage-url=\"/api/v2/package/Contoso.Utils/1.0.0\">Relist", package);
        Assert.Contains("id=\"deprecation-form\"", package);
        Assert.Contains("id=\"vulnerability-form\"", package);
        Assert.Contains("js/manage.js", package);
        Assert.Contains("href=\"/admin\"", package);
    }

    [Fact]
    public async Task ManagersSeePackagesWhoseVersionsAreAllUnlisted()
    {
        (WebApplication app, Uri address) = await StartAsync(new() { ["Feed:PackageDeletionBehavior"] = "Unlist" });
        await AddPackageAsync(app, "Contoso.Hidden", "1.0.0");
        HttpClient alice = await SignUpAsync(address, "alice");
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync("api/v2/package/Contoso.Hidden/1.0.0")).StatusCode);

        string managed = await GetPageAsync(alice, "packages/Contoso.Hidden");
        string anonymous = await GetPageAsync(CreateBrowser(address), "packages/Contoso.Hidden");

        Assert.Contains("This version is unlisted", managed);
        Assert.Contains(">Relist</button>", managed);
        Assert.Contains("Could not find package", anonymous);
    }

    [Fact]
    public async Task DeletingPermanentlyIsADeleteButton()
    {
        (WebApplication app, Uri address) = await StartAsync(new() { ["Feed:PackageDeletionBehavior"] = "HardDelete" });
        await AddPackageAsync(app, "Contoso.Utils", "1.0.0");
        HttpClient alice = await SignUpAsync(address, "alice");

        string package = await GetPageAsync(alice, "packages/Contoso.Utils");

        Assert.Contains(">Delete</button>", package);
        Assert.DoesNotContain(">Unlist</button>", package);
    }

    [Fact]
    public async Task WritersUploadButDontManage()
    {
        (WebApplication app, Uri address) = await StartAsync(new() { ["Feed:Authorization:Users:DefaultRole"] = "ReadWrite", ["Feed:PackageDeletionBehavior"] = "Unlist" });
        await AddPackageAsync(app, "Contoso.Utils", "1.0.0");
        await SignUpAsync(address, "alice");
        HttpClient bob = await SignUpAsync(address, "bob");

        string upload = await GetPageAsync(bob, "upload");
        string package = await GetPageAsync(bob, "packages/Contoso.Utils");

        Assert.Contains("id=\"upload-form\"", upload);
        Assert.Contains(">Unlist</button>", package);
        Assert.DoesNotContain("deprecation-form", package);
        Assert.DoesNotContain("vulnerability-form", package);
        Assert.DoesNotContain("href=\"/admin\"", package);
    }

    [Fact]
    public async Task UploadsFromTheBrowserArePushes()
    {
        (_, Uri address) = await StartAsync();
        HttpClient alice = await SignUpAsync(address, "alice");

        // What manage.js sends.
        using MultipartFormDataContent content = new() { { new StreamContent(TestPackages.Create("Contoso.Uploaded", "1.0.0")), "package", "Contoso.Uploaded.1.0.0.nupkg" } };
        using HttpResponseMessage response = await alice.PutAsync("api/v2/package", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("Contoso.Uploaded", await GetPageAsync(alice, "packages/Contoso.Uploaded"));
    }

    [Fact]
    public async Task ReadOnlyModeLeavesOnlyAdministration()
    {
        (WebApplication app, Uri address) = await StartAsync(new() { ["Feed:IsReadOnlyMode"] = "true" });
        await AddPackageAsync(app, "Contoso.Utils", "1.0.0");
        HttpClient alice = await SignUpAsync(address, "alice");

        string upload = await GetPageAsync(alice, "upload");
        string package = await GetPageAsync(alice, "packages/Contoso.Utils");

        Assert.Contains("The feed is read-only", upload);
        Assert.DoesNotContain("upload-form", upload);
        Assert.DoesNotContain("manage-package", package);
        Assert.Contains("href=\"/admin\"", package);
        Assert.Contains("id=\"admin-page\"", await GetPageAsync(alice, "admin"));
    }

    [Fact]
    public async Task WithoutUsersTheGalleryOnlyBrowses()
    {
        (WebApplication app, Uri address) = await StartAsync(users: false);
        await AddPackageAsync(app, "Contoso.Utils", "1.0.0");
        HttpClient visitor = CreateBrowser(address);

        string package = await GetPageAsync(visitor, "packages/Contoso.Utils");
        string upload = await GetPageAsync(visitor, "upload");

        Assert.DoesNotContain("manage-package", package);
        Assert.DoesNotContain("upload-form", upload);
        Assert.DoesNotContain("account/signin", package);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("admin")).StatusCode);
    }

    private async Task<(WebApplication App, Uri Address)> StartAsync(Dictionary<string, string?>? settings = null, bool users = true)
    {
        string testId = Guid.NewGuid().ToString("N");
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new XunitLoggerProvider(Output));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Feed:Database:ConnectionString"] = $"Data Source=GalleryManagement-{testId};Mode=Memory;Cache=Shared",
            ["Feed:Storage:Path"] = Path.Combine(Path.GetTempPath(), "NuDepotTests", testId, "Packages"),
            ["Feed:Search:Type"] = "Database",
        });
        builder.Configuration.AddInMemoryCollection(settings ?? []);

        Core.Hosting.NuDepotBuilder nuDepot = builder.Services
            .AddNuDepot()
            .AddFeed();
        if (users)
            nuDepot.AddUsers();
        nuDepot.AddGallery();

        WebApplication app = builder.Build();
        Core.Hosting.NuDepotApplicationBuilder pipeline = app
            .UseNuDepot()
            .UseFeed();
        if (users)
            pipeline.MapUsers();
        pipeline.UseGallery();

        Apps.Add(app);
        await app.RunMigrationsAsync();
        await app.StartAsync();

        // Browsers reach it at localhost, which passkeys allow over HTTP.
        Uri listening = new(app.Urls.First());
        return (app, new Uri($"http://localhost:{listening.Port}/"));
    }

    private static async Task AddPackageAsync(WebApplication app, string id, string version)
    {
        using IServiceScope scope = app.Services.CreateScope();
        PackageIndexingResult result = await scope.ServiceProvider
            .GetRequiredService<IPackageIndexingService>()
            .IndexAsync(TestPackages.Create(id, version), CancellationToken.None);
        Assert.Equal(PackageIndexingResult.Success, result);
    }

    private HttpClient CreateBrowser(Uri address)
    {
        HttpClient browser = new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = address };
        Disposables.Add(browser);
        return browser;
    }

    // Signs up the way the gallery's sign-in page does.
    private async Task<HttpClient> SignUpAsync(Uri address, string userName)
    {
        HttpClient browser = CreateBrowser(address);
        TestAuthenticator authenticator = new(address.GetLeftPart(UriPartial.Authority));
        Disposables.Add(authenticator);

        using HttpResponseMessage options = await browser.PostAsync("api/account/signup", JsonContent.Create(new { userName, passkeyName = "Laptop" }));
        string optionsJson = await options.Content.ReadAsStringAsync();
        Assert.True(options.IsSuccessStatusCode, optionsJson);

        using HttpResponseMessage created = await browser.PostAsync(
            $"api/account/passkeys?userName={userName}",
            new StringContent(authenticator.CreateCredential(optionsJson), Encoding.UTF8, "application/json"));
        Assert.True(created.StatusCode == HttpStatusCode.NoContent, await created.Content.ReadAsStringAsync());

        return browser;
    }

    private static async Task<string> GetPageAsync(HttpClient browser, string url)
    {
        using HttpResponseMessage response = await browser.GetAsync(url);
        string html = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode} {html}");
        return html;
    }
}
