using Microsoft.Extensions.DependencyInjection;
using NuDepot.Feed.Signing;
using NuDepot.Feed.Tests.Support;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Tests what the gallery's management uses: the signed-in user's permissions, their signed-in browsers, and the
/// feed's administration.
/// </summary>
public class AdminIntegrationTests(ITestOutputHelper output) : IDisposable
{
    private ITestOutputHelper Output { get; } = output;
    private List<IDisposable> Disposables { get; } = [];

    public void Dispose()
    {
        foreach (IDisposable disposable in Disposables)
            disposable.Dispose();
    }

    [Fact]
    public async Task TheAccountSaysWhatTheUserMayDo()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient alice, _) = await SignUpAsync(app, "alice");
        (HttpClient bob, _) = await SignUpAsync(app, "bob");

        JsonElement admin = await GetJsonAsync(alice, "api/account");
        Assert.Equal(["Read", "Push", "Unlist", "Deprecate", "HardDelete", "ManageVulnerabilities", "Administer"], Strings(admin.GetProperty("permissions")));
        Assert.Equal("HardDelete", admin.GetProperty("packageDeletion").GetString());

        Assert.Equal(["Read"], Strings((await GetJsonAsync(bob, "api/account")).GetProperty("permissions")));
    }

    [Fact]
    public async Task ReadOnlyModeLeavesAdministration()
    {
        NuDepotTestApplication app = CreateApp(new() { ["IsReadOnlyMode"] = "true", ["PackageDeletionBehavior"] = "Unlist" });
        (HttpClient alice, _) = await SignUpAsync(app, "alice");

        JsonElement account = await GetJsonAsync(alice, "api/account");

        Assert.Equal(["Read", "Administer"], Strings(account.GetProperty("permissions")));
        Assert.Equal("Unlist", account.GetProperty("packageDeletion").GetString());
    }

    [Fact]
    public async Task UsersSeeAndSignOutTheirBrowsers()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient laptop, TestAuthenticator authenticator) = await SignUpAsync(app, "alice");
        HttpClient phone = await SignInAsync(app, "alice", authenticator);

        JsonElement sessions = await GetJsonAsync(laptop, "api/account/sessions");
        Assert.Equal(2, sessions.GetArrayLength());
        JsonElement other = sessions.EnumerateArray().Single(s => !s.GetProperty("isCurrent").GetBoolean());

        using HttpResponseMessage response = await laptop.DeleteAsync($"api/account/sessions/{other.GetProperty("id").GetInt32()}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False((await GetJsonAsync(phone, "api/account")).GetProperty("signedIn").GetBoolean());
        Assert.True((await GetJsonAsync(laptop, "api/account")).GetProperty("signedIn").GetBoolean());
    }

    [Fact]
    public async Task UsersCantSignOutOthersBrowsers()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient alice, _) = await SignUpAsync(app, "alice");
        (HttpClient bob, _) = await SignUpAsync(app, "bob");
        int bobsSession = (await GetJsonAsync(bob, "api/account/sessions"))[0].GetProperty("id").GetInt32();

        using HttpResponseMessage response = await alice.DeleteAsync($"api/account/sessions/{bobsSession}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True((await GetJsonAsync(bob, "api/account")).GetProperty("signedIn").GetBoolean());
    }

    [Fact]
    public async Task AdminsListUsersAndChangeTheirRoles()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient alice, _) = await SignUpAsync(app, "alice");
        (HttpClient bob, _) = await SignUpAsync(app, "bob");

        JsonElement users = await GetJsonAsync(alice, "api/admin/users");
        Assert.Equal(["alice", "bob"], users.EnumerateArray().Select(u => u.GetProperty("userName").GetString()));
        JsonElement bobRow = users[1];
        Assert.Equal("ReadOnly", bobRow.GetProperty("role").GetString());
        Assert.Equal(1, bobRow.GetProperty("passkeys").GetInt32());
        Assert.Equal(1, bobRow.GetProperty("sessions").GetInt32());
        Assert.True(users[0].GetProperty("isCurrentUser").GetBoolean());

        using HttpResponseMessage response = await alice.PutAsync($"api/admin/users/{bobRow.GetProperty("id").GetInt32()}", Json("""{ "role": "ReadWrite" }"""));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("ReadWrite", (await GetJsonAsync(bob, "api/account")).GetProperty("role").GetString());
    }

    [Theory]
    [InlineData("""{ "role": "None" }""", HttpStatusCode.BadRequest)]
    [InlineData("""{ "role": "Owner" }""", HttpStatusCode.BadRequest)]
    [InlineData("""{ }""", HttpStatusCode.BadRequest)]
    [InlineData("not json", HttpStatusCode.BadRequest)]
    [InlineData("""{ "role": "readwrite" }""", HttpStatusCode.NoContent)]
    public async Task UsersOnlyGetTheFeedsRoles(string body, HttpStatusCode expected)
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient alice, _) = await SignUpAsync(app, "alice");
        await SignUpAsync(app, "bob");
        int bobId = (await GetJsonAsync(alice, "api/admin/users"))[1].GetProperty("id").GetInt32();

        using HttpResponseMessage response = await alice.PutAsync($"api/admin/users/{bobId}", Json(body));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task ChangingAMissingUserIsNotFound()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient alice, _) = await SignUpAsync(app, "alice");

        using HttpResponseMessage response = await alice.PutAsync("api/admin/users/999", Json("""{ "role": "ReadWrite" }"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OnlyAdminsAdministerTheFeed()
    {
        NuDepotTestApplication app = CreateApp();
        await SignUpAsync(app, "alice");
        (HttpClient bob, _) = await SignUpAsync(app, "bob");

        foreach (string url in (string[])["api/admin/users", "api/admin/status", "api/admin/signing"])
        {
            using HttpResponseMessage response = await bob.GetAsync(url);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task TheLastAdminStaysAnAdmin()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient alice, _) = await SignUpAsync(app, "alice");
        int aliceId = (await GetJsonAsync(alice, "api/admin/users"))[0].GetProperty("id").GetInt32();

        using HttpResponseMessage demote = await alice.PutAsync($"api/admin/users/{aliceId}", Json("""{ "role": "ReadOnly" }"""));
        using HttpResponseMessage delete = await alice.DeleteAsync($"api/admin/users/{aliceId}");

        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal("Admin", (await GetJsonAsync(alice, "api/account")).GetProperty("role").GetString());
    }

    [Fact]
    public async Task DeletingAUserSignsThemOutAndRevokesTheirKeys()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient alice, _) = await SignUpAsync(app, "alice");
        (HttpClient bob, _) = await SignUpAsync(app, "bob");
        string bobsKey = await CreateApiKeyAsync(bob);
        int bobId = (await GetJsonAsync(alice, "api/admin/users"))[1].GetProperty("id").GetInt32();

        using HttpResponseMessage response = await alice.DeleteAsync($"api/admin/users/{bobId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False((await GetJsonAsync(bob, "api/account")).GetProperty("signedIn").GetBoolean());
        Assert.Equal(["alice"], (await GetJsonAsync(alice, "api/admin/users")).EnumerateArray().Select(u => u.GetProperty("userName").GetString()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await ReadWithKeyAsync(app, "bob", bobsKey)).StatusCode);
    }

    [Fact]
    public async Task AdminsSignUsersOutAndRevokeTheirKeys()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient alice, _) = await SignUpAsync(app, "alice");
        (HttpClient bob, _) = await SignUpAsync(app, "bob");
        string bobsKey = await CreateApiKeyAsync(bob);
        int bobId = (await GetJsonAsync(alice, "api/admin/users"))[1].GetProperty("id").GetInt32();

        using HttpResponseMessage signOut = await alice.DeleteAsync($"api/admin/users/{bobId}/sessions");
        using HttpResponseMessage revoke = await alice.DeleteAsync($"api/admin/users/{bobId}/apikeys");

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.False((await GetJsonAsync(bob, "api/account")).GetProperty("signedIn").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await ReadWithKeyAsync(app, "bob", bobsKey)).StatusCode);
    }

    [Fact]
    public async Task AdminsSeeTheFeedsSettingsAndMirrors()
    {
        NuDepotTestApplication app = new(Output, new HttpClient(), new Dictionary<string, string>
        {
            ["Authorization:ApiKeys:Admin"] = "admin-key",
            ["Mirroring:Mirrors:0:WhenUnreachable"] = "Stop",
        }, users: true);
        Disposables.Add(app);
        (HttpClient alice, _) = await SignUpAsync(app, "alice");

        JsonElement status = await GetJsonAsync(alice, "api/admin/status");

        JsonElement health = status.GetProperty("health");
        Assert.Equal("Healthy", health.GetProperty("status").GetString());
        Assert.Equal(["database", "storage", "signing", "mirrors"], health.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        Assert.Equal("HardDelete", status.GetProperty("packageDeletionBehavior").GetString());
        Assert.Equal(["Admin"], Strings(status.GetProperty("apiKeyRoles")));
        Assert.Equal("ReadOnly", status.GetProperty("anonymousRole").GetString());
        Assert.True(status.GetProperty("isMirroring").GetBoolean());
        JsonElement mirror = Assert.Single(status.GetProperty("mirrors").EnumerateArray());
        Assert.Equal("Upstream", mirror.GetProperty("name").GetString());
        Assert.Equal("Stop", mirror.GetProperty("whenUnreachable").GetString());
        Assert.Equal(JsonValueKind.Null, mirror.GetProperty("unreachableUntil").ValueKind);
        Assert.DoesNotContain("admin-key", status.GetRawText());
    }

    [Fact]
    public async Task AdminsReplaceTheGeneratedSigningCertificate()
    {
        NuDepotTestApplication app = new(
            Output,
            configuration: new Dictionary<string, string> { ["RepositorySigning:IsEnabled"] = "true" },
            users: true,
            configureServices: services => services.AddSingleton(new GeneratedCertificateKeySize(2048)));
        Disposables.Add(app);

        // A feed that signs packages only answers HTTPS.
        (HttpClient alice, _) = await SignUpAsync(app, "alice", https: true);

        JsonElement signing = await GetJsonAsync(alice, "api/admin/signing");
        Assert.True(signing.GetProperty("generatesCertificates").GetBoolean());
        JsonElement original = Assert.Single(signing.GetProperty("certificates").EnumerateArray());
        Assert.True(original.GetProperty("isCurrent").GetBoolean());

        using HttpResponseMessage response = await alice.PostAsync("api/admin/signing/rotate", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement[] certificates = [.. (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("certificates").EnumerateArray()];
        Assert.Equal(2, certificates.Length);
        Assert.True(certificates[0].GetProperty("isCurrent").GetBoolean());
        Assert.Equal(original.GetProperty("fingerprint").GetString(), certificates[1].GetProperty("fingerprint").GetString());
        Assert.NotEqual(JsonValueKind.Null, certificates[1].GetProperty("revokedAt").ValueKind);
    }

    [Fact]
    public async Task OnlyGeneratedCertificatesAreReplaced()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient alice, _) = await SignUpAsync(app, "alice");

        Assert.False((await GetJsonAsync(alice, "api/admin/signing")).GetProperty("isEnabled").GetBoolean());
        using HttpResponseMessage response = await alice.PostAsync("api/admin/signing/rotate", content: null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task VulnerabilityManagersListAPackagesAdvisories()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient alice, _) = await SignUpAsync(app, "alice");
        using (HttpResponseMessage added = await alice.PutAsync("api/vulnerabilities", Json("""{ "packageId": "Contoso.Utils", "versions": "(, 1.2.0)", "url": "https://example.com/1", "severity": 2 }""")))
        {
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        }

        JsonElement advisory = Assert.Single((await GetJsonAsync(alice, "api/vulnerabilities?packageId=contoso.utils")).EnumerateArray());

        Assert.Equal("(, 1.2.0)", advisory.GetProperty("versions").GetString());
        Assert.Equal("https://example.com/1", advisory.GetProperty("url").GetString());
        Assert.Equal(2, advisory.GetProperty("severity").GetInt32());
    }

    private NuDepotTestApplication CreateApp(Dictionary<string, string>? configuration = null)
    {
        NuDepotTestApplication app = new(Output, configuration: configuration, users: true);
        Disposables.Add(app);
        return app;
    }

    private HttpClient CreateBrowser(NuDepotTestApplication app, bool https)
    {
        HttpClient browser = app.CreateBrowserClient();
        if (https)
            browser.BaseAddress = new Uri("https://localhost/");

        Disposables.Add(browser);
        return browser;
    }

    private async Task<(HttpClient Browser, TestAuthenticator Authenticator)> SignUpAsync(NuDepotTestApplication app, string userName, bool https = false)
    {
        HttpClient browser = CreateBrowser(app, https);
        TestAuthenticator authenticator = new(https ? "https://localhost" : "http://localhost");
        Disposables.Add(authenticator);

        string options = await PostAsync(browser, "api/account/signup", new { userName, passkeyName = "Laptop" });
        using HttpResponseMessage response = await browser.PostAsync($"api/account/passkeys?userName={userName}", Json(authenticator.CreateCredential(options)));
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        return (browser, authenticator);
    }

    private async Task<HttpClient> SignInAsync(NuDepotTestApplication app, string userName, TestAuthenticator authenticator)
    {
        HttpClient browser = CreateBrowser(app, https: false);
        string options = await PostAsync(browser, "api/account/signin/options", new { userName });
        using HttpResponseMessage response = await browser.PostAsync("api/account/signin", Json(authenticator.GetAssertion(options)));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        return browser;
    }

    private static async Task<string> CreateApiKeyAsync(HttpClient browser)
    {
        using HttpResponseMessage response = await browser.PostAsync("api/account/apikeys", JsonContent.Create(new { name = "Build" }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString()!;
    }

    // Reads the feed with a user's API key, as a NuGet client does with a package source's credentials.
    private static async Task<HttpResponseMessage> ReadWithKeyAsync(NuDepotTestApplication app, string userName, string key)
    {
        using HttpClient client = app.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Put, "api/v2/package");
        request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{key}")));
        request.Content = new MultipartFormDataContent { { new StreamContent(TestPackages.Create("Keyed", "1.0.0")), "package", "package.nupkg" } };
        return await client.SendAsync(request);
    }

    private static async Task<string> PostAsync(HttpClient client, string url, object body)
    {
        using HttpResponseMessage response = await client.PostAsync(url, JsonContent.Create(body));
        string content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, content);
        return content;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        using HttpResponseMessage response = await client.GetAsync(url);
        string content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {content}");
        return JsonDocument.Parse(content).RootElement.Clone();
    }

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
