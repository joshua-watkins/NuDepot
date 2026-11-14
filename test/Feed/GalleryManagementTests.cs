using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NuDepot.Core;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Signing;
using NuDepot.Feed.Tests.Support;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Tests the gallery hosted with the feed and its users, the way the Windows and Linux services host them: what each
/// visitor may manage, and that nobody else sees it. The gallery calls the feed over the server's address, so the
/// application listens on a real port.
/// </summary>
public sealed partial class GalleryManagementTests(ITestOutputHelper output) : IAsyncDisposable
{
    private ITestOutputHelper Output { get; } = output;
    private List<WebApplication> Apps { get; } = [];
    private List<IDisposable> Disposables { get; } = [];
    private Dictionary<HttpClient, CookieContainer> Cookies { get; } = [];

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
        using HttpResponseMessage admin = await visitor.GetAsync("admin", TestContext.Current.CancellationToken);

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
        using HttpResponseMessage admin = await bob.GetAsync("admin", TestContext.Current.CancellationToken);

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
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync("api/v2/package/Contoso.Utils/1.0.0", TestContext.Current.CancellationToken)).StatusCode);

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
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync("api/v2/package/Contoso.Hidden/1.0.0", TestContext.Current.CancellationToken)).StatusCode);

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
        using HttpResponseMessage response = await alice.PutAsync("api/v2/package", content, TestContext.Current.CancellationToken);

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

        // NuGet only pushes to an HTTP source when it's told to.
        Assert.Contains($">dotnet nuget push -s {address}v3/index.json --allow-insecure-connections package.nupkg<", upload);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("admin", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task TheGalleryReadsAFeedThatSignsOverHttps()
    {
        (_, Uri address) = await StartAsync(new() { ["Feed:Authorization:ApiKeys:Admin"] = "admin-key" }, signing: true);

        // Through a reverse proxy that serves HTTPS, as a feed that signs requires.
        HttpClient admin = CreateBrowser(address);
        admin.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        admin.DefaultRequestHeaders.Add("X-NuGet-ApiKey", "admin-key");
        using MultipartFormDataContent content = new() { { new StreamContent(TestPackages.Create("Contoso.Utils", "1.0.0")), "package", "package.nupkg" } };
        Assert.Equal(HttpStatusCode.Created, (await admin.PutAsync("api/v2/package", content, TestContext.Current.CancellationToken)).StatusCode);

        // The gallery tells the feed the browser used HTTPS.
        HttpClient visitor = CreateBrowser(address);
        visitor.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        string page = await GetPageAsync(visitor, "packages/Contoso.Utils");

        Assert.Contains("Contoso.Utils", page);
        Assert.Contains($"href=\"https://localhost:{address.Port}/v3/package/contoso.utils/1.0.0/contoso.utils.1.0.0.nupkg\"", page);
        Assert.Contains($">dotnet nuget push -s https://localhost:{address.Port}/v3/index.json package.nupkg<", await GetPageAsync(visitor, "upload"));

        // Over HTTP, the feed refuses it.
        using HttpResponseMessage http = await CreateBrowser(address).GetAsync("packages/Contoso.Utils", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, http.StatusCode);
    }

    [Theory]
    [InlineData("/packages/Contoso.Utils", "/packages/Contoso.Utils")]
    [InlineData("/", "/")]
    // Signing in never sends the visitor to another site.
    [InlineData("https://phishing.example/", "/")]
    [InlineData("//phishing.example/", "/")]
    [InlineData("/\\phishing.example/", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData(null, "/")]
    public async Task SigningInOnlyReturnsToThisSite(string? returnUrl, string expected)
    {
        (_, Uri address) = await StartAsync();
        HttpClient visitor = CreateBrowser(address);

        string url = returnUrl == null ? "account/signin" : "account/signin?returnUrl=" + Uri.EscapeDataString(returnUrl);
        string page = await GetPageAsync(visitor, url);

        Assert.Contains("id=\"signin-page\"", page);
        Assert.Equal(expected, GetSetting(page, "account-settings", "returnUrl"));
    }

    [Fact]
    public async Task TheAccountPageNeedsASignedInUser()
    {
        (_, Uri address) = await StartAsync();
        HttpClient visitor = CreateBrowser(address);

        using HttpResponseMessage anonymous = await visitor.GetAsync("account?tab=keys", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal("/account/signin?returnUrl=" + Uri.EscapeDataString("/account?tab=keys"), anonymous.Headers.Location!.OriginalString);

        HttpClient alice = await SignUpAsync(address, "alice");
        string page = await GetPageAsync(alice, "account");
        Assert.Contains("id=\"account-page\"", page);
        Assert.Contains("<span id=\"account-name\">alice</span>", page);
    }

    [Fact]
    public async Task AFeedClosedToVisitorsAsksThemToSignIn()
    {
        (WebApplication app, Uri address) = await StartAsync(new() { ["Feed:Authorization:Users:AnonymousReads"] = "false" });
        await AddPackageAsync(app, "Contoso.Utils", "1.0.0");

        using HttpResponseMessage response = await CreateBrowser(address).GetAsync("packages/Contoso.Utils", TestContext.Current.CancellationToken);
        string page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("href=\"/account/signin?returnUrl=" + Uri.EscapeDataString("/packages/Contoso.Utils") + "\"", page);

        // Once they sign in, they can read it.
        HttpClient alice = await SignUpAsync(address, "alice");
        Assert.Contains("Contoso.Utils", await GetPageAsync(alice, "packages/Contoso.Utils"));
    }

    // A setting from one of the page's settings scripts.
    private static string? GetSetting(string page, string id, string name)
    {
        Match settings = Regex.Match(page, $"<script type=\"application/json\" id=\"{id}\">(.*?)</script>");
        Assert.True(settings.Success, page);

        using JsonDocument document = JsonDocument.Parse(settings.Groups[1].Value);
        return document.RootElement
            .EnumerateObject()
            .Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            .Value
            .GetString();
    }

    [Fact]
    public async Task PageLoadsKeepTheUserSignedIn()
    {
        TestClock clock = new();
        (WebApplication app, Uri address) = await StartAsync(clock: clock);
        await AddPackageAsync(app, "Contoso.Utils", "1.0.0");
        HttpClient alice = await SignUpAsync(address, "alice");
        string[] files = await GetPageFilesAsync(alice, address, "packages/Contoso.Utils");
        Assert.True(files.Length > 10, string.Join(", ", files));

        // Each load sends the page and its files at once, with the cookie the browser has: the gallery renders the
        // page with its own requests to the feed, and every file request authenticates too. The token is replaced
        // during the first load after 15 minutes.
        for (int load = 0; load < 3; load++)
        {
            clock.Advance(TimeSpan.FromMinutes(16));
            await LoadPageAsync(alice, "packages/Contoso.Utils", files);
        }

        // A load whose responses never arrived, like when Alice leaves the page while it loads, replaced the token.
        clock.Advance(TimeSpan.FromMinutes(16));
        // Its client drops the new cookie, so it never uses it either.
        using (HttpClient lost = new(new HttpClientHandler { UseCookies = false }) { BaseAddress = address })
        {
            lost.DefaultRequestHeaders.Add("Cookie", Cookies[alice].GetCookieHeader(address));
            await Task.WhenAll(files.Select(file => lost.GetAsync(file)));
        }

        // Her next load, long after, still has the old cookie, and keeps her signed in.
        clock.Advance(TimeSpan.FromMinutes(5));
        await LoadPageAsync(alice, "packages/Contoso.Utils", files);
        clock.Advance(TimeSpan.FromMinutes(5));
        await LoadPageAsync(alice, "packages/Contoso.Utils", files);
        clock.Advance(TimeSpan.FromMinutes(16));
        await LoadPageAsync(alice, "packages/Contoso.Utils", files);
    }

    // Loads a page and its files at once, like a browser, and checks the page is still shown to its signed-in Admin.
    private static async Task LoadPageAsync(HttpClient browser, string url, string[] files)
    {
        Task<string> page = GetPageAsync(browser, url);
        HttpResponseMessage[] responses = await Task.WhenAll(files.Select(file => browser.GetAsync(file)));
        foreach (HttpResponseMessage response in responses)
        {
            Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage!.RequestUri}: {(int)response.StatusCode}");
            response.Dispose();
        }

        Assert.Contains("href=\"/admin\"", await page);
        AccountState account = await browser.GetFromJsonAsync<AccountState>("api/account") ?? new();
        Assert.True(account.SignedIn);
    }

    // The gallery's files a page loads: its stylesheets and scripts, and the icons its stylesheet uses.
    private static async Task<string[]> GetPageFilesAsync(HttpClient browser, Uri address, string url)
    {
        string html = await GetPageAsync(browser, url);
        List<Uri> files = [.. GalleryFilePattern()
            .Matches(html)
            .Select(m => new Uri(address, WebUtility.HtmlDecode(m.Groups[1].Value)))];

        foreach (Uri stylesheet in files.Where(f => f.AbsolutePath.EndsWith("site.css", StringComparison.Ordinal)).ToList())
        {
            string css = await browser.GetStringAsync(stylesheet);
            files.AddRange(StylesheetUrlPattern()
                .Matches(css)
                .Select(m => new Uri(stylesheet, m.Groups[1].Value)));
        }

        return [.. files.Select(f => f.PathAndQuery).Distinct()];
    }

    // A page's links to the gallery's own files.
    [GeneratedRegex("(?:src|href)=\"(/_content/[^\"]+)\"")]
    private static partial Regex GalleryFilePattern();

    // The files a stylesheet uses.
    [GeneratedRegex("url\\(\"([^\"]+)\"\\)")]
    private static partial Regex StylesheetUrlPattern();

    private sealed class AccountState
    {
        public bool SignedIn { get; set; }
    }

    private async Task<(WebApplication App, Uri Address)> StartAsync(Dictionary<string, string?>? settings = null, bool users = true, TestClock? clock = null, bool signing = false)
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
        if (signing)
        {
            // Smaller generated keys than the feed's, so they're made quickly.
            nuDepot.AddSigning();
            builder.Services.AddSingleton(new GeneratedCertificateKeySize(2048));
        }

        nuDepot.AddGallery();

        // A clock the test moves forward, for the sessions and their cookies.
        if (clock != null)
        {
            builder.Services.AddSingleton<TimeProvider>(clock);
            builder.Services.Configure<CookieAuthenticationOptions>(NuDepotAuthenticationSchemes.User, options => options.TimeProvider = clock);
        }

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
        CookieContainer cookies = new();
        HttpClient browser = new(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false }) { BaseAddress = address };
        Cookies[browser] = cookies;
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
