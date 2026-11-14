using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NuDepot.Feed.Tests.Support;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace NuDepot.Feed.Tests;

/// <summary>
/// The feed's logs say who changed it, and show keys that aren't valid and requests that were refused, without the
/// keys themselves.
/// </summary>
public sealed class FeedLoggingTests(ITestOutputHelper output) : IDisposable
{
    private const string ReadOnlyKey = "read-only-key-0123456789";
    private const string ReadWriteKey = "read-write-key-0123456789";
    private const string AdminKey = "admin-key-0123456789";

    private LogRecorder Logs { get; } = new();

    private NuDepotTestApplication App => field ??= new(
        output,
        configuration: new Dictionary<string, string>
        {
            ["Authorization:ApiKeys:ReadOnly"] = ReadOnlyKey,
            ["Authorization:ApiKeys:ReadWrite"] = ReadWriteKey,
            ["Authorization:ApiKeys:Admin"] = AdminKey,
            ["PackageDeletionBehavior"] = "Unlist",
        },
        configureServices: services => services.AddSingleton<ILoggerProvider>(Logs));

    public void Dispose() => App.Dispose();

    [Fact]
    public async Task ChangesToPackagesSayWhoMadeThem()
    {
        using HttpClient client = Client(AdminKey);

        Assert.Equal(HttpStatusCode.Created, (await client.PutAsync("api/v2/package", Package("Logged.Package"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("api/v2/package/Logged.Package/1.0.0")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("api/v2/package/Logged.Package/1.0.0", null)).StatusCode);
        Assert.True((await client.PutAsJsonAsync("api/package/Logged.Package/1.0.0/deprecation", new { reasons = (string[])["Legacy"] })).IsSuccessStatusCode);
        Assert.True((await client.DeleteAsync("api/package/Logged.Package/1.0.0/deprecation")).IsSuccessStatusCode);
        Assert.True((await client.PutAsJsonAsync("api/vulnerabilities", new
        {
            packageId = "Logged.Package",
            versions = "[1.0.0, 2.0.0)",
            url = "https://example.com/advisory",
            severity = 2,
        })).IsSuccessStatusCode);

        IReadOnlyList<string> information = Logs.Messages(LogLevel.Information);
        foreach (string change in (string[])
        [
            "pushed package Logged.Package 1.0.0",
            "unlisted package Logged.Package 1.0.0",
            "relisted package Logged.Package 1.0.0",
            "deprecated package Logged.Package 1.0.0 as Legacy",
            "undeprecated package Logged.Package 1.0.0",
            "saved the vulnerability advisory https://example.com/advisory for package Logged.Package [1.0.0, 2.0.0), with severity 2",
        ])
        {
            Assert.Contains(information, m => m.StartsWith("The Admin API key (", StringComparison.Ordinal) && m.EndsWith(change, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task KeysThatArentValidAreLoggedWithoutTheKey()
    {
        using HttpClient header = Client("guessed-key-1");
        using HttpClient credentials = App.CreateClient();
        credentials.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("ReadWrite:guessed-key-2")));

        await header.GetAsync("v3/index.json");
        await credentials.GetAsync("v3/index.json");

        IReadOnlyList<string> warnings = Logs.Messages(LogLevel.Warning);
        Assert.Contains(warnings, m => m.EndsWith("sent an API key that isn't valid", StringComparison.Ordinal));
        Assert.Contains(warnings, m => m.EndsWith("sent credentials that aren't valid, for ReadWrite", StringComparison.Ordinal));
        Assert.DoesNotContain(Logs.Entries, entry => entry.Message.Contains("guessed-key", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeysWithoutTheRoleAreLoggedWhenRefused()
    {
        using HttpClient client = Client(ReadOnlyKey);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync("api/v2/package", Package("Refused.Package"))).StatusCode);

        Assert.Contains(Logs.Messages(LogLevel.Warning), m =>
            m.StartsWith("The ReadOnly API key (", StringComparison.Ordinal) &&
            m.EndsWith("may not Push, so PUT /api/v2/package was refused", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnonymousRequestsAskedForAKeyArentLogged()
    {
        using HttpClient client = App.CreateClient();

        // NuGet clients are asked for credentials before they send them.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("v3/index.json")).StatusCode);

        Assert.DoesNotContain(Logs.Messages(LogLevel.Warning), m => m.Contains("refused", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheSettingsAreLoggedAtStartup()
    {
        using HttpClient client = App.CreateClient();
        await client.GetAsync("health");

        Assert.Contains(
            "Requests without a key or user may do what None may. Users: off. Repository signing: off. Mirrors: off. Read-only mode: off.",
            Logs.Messages(LogLevel.Information));
    }

    private HttpClient Client(string key)
    {
        HttpClient client = App.CreateClient();
        client.DefaultRequestHeaders.Add("X-NuGet-ApiKey", key);
        return client;
    }

    private static MultipartFormDataContent Package(string id) => new()
    {
        { new StreamContent(TestPackages.Create(id, "1.0.0")), "package", "package.nupkg" },
    };
}
