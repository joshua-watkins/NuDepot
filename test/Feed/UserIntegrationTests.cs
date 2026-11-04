using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Tests.Support;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Tests users who sign up and sign in with passkeys, and their API keys.
/// </summary>
public class UserIntegrationTests(ITestOutputHelper output) : IDisposable
{
    private ITestOutputHelper Output { get; } = output;
    private List<IDisposable> Disposables { get; } = [];

    public void Dispose()
    {
        foreach (IDisposable disposable in Disposables)
            disposable.Dispose();
    }

    [Fact]
    public async Task FirstUserIsAnAdminAndOthersGetTheDefaultRole()
    {
        NuDepotTestApplication app = CreateApp();

        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");
        (HttpClient? bob, TestAuthenticator _) = await SignUpAsync(app, "bob");

        Assert.Equal("Admin", (await GetAccountAsync(alice)).GetProperty("role").GetString());
        Assert.Equal("ReadOnly", (await GetAccountAsync(bob)).GetProperty("role").GetString());
    }

    [Fact]
    public async Task SignsInWithThePasskey()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient _, TestAuthenticator? authenticator) = await SignUpAsync(app, "alice");

        using HttpClient browser = app.CreateBrowserClient();
        Assert.False((await GetAccountAsync(browser)).GetProperty("signedIn").GetBoolean());

        string options = await PostAsync(browser, "api/account/signin/options", new { userName = "ALICE" });
        HttpResponseMessage signIn = await browser.PostAsync("api/account/signin", Json(authenticator.GetAssertion(options)));

        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        JsonElement account = await GetAccountAsync(browser);
        Assert.True(account.GetProperty("signedIn").GetBoolean());
        Assert.Equal("alice", account.GetProperty("userName").GetString());
    }

    [Fact]
    public async Task EachChallengeCanOnlyBeAnsweredOnce()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient _, TestAuthenticator? authenticator) = await SignUpAsync(app, "alice");

        using HttpClient browser = app.CreateBrowserClient();
        string options = await PostAsync(browser, "api/account/signin/options", new { userName = "alice" });
        string assertion = authenticator.GetAssertion(options);

        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("api/account/signin", Json(assertion))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("api/account/signin", Json(assertion))).StatusCode);
    }

    [Fact]
    public async Task EachUserHasOneSignInInProgress()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient _, TestAuthenticator? authenticator) = await SignUpAsync(app, "alice");

        // The challenge isn't kept by the browser, so another browser's sign-in replaces it.
        using HttpClient first = app.CreateBrowserClient();
        using HttpClient second = app.CreateBrowserClient();
        string firstOptions = await PostAsync(first, "api/account/signin/options", new { userName = "alice" });
        string secondOptions = await PostAsync(second, "api/account/signin/options", new { userName = "alice" });

        HttpResponseMessage replaced = await first.PostAsync("api/account/signin", Json(authenticator.GetAssertion(firstOptions)));
        Assert.Equal(HttpStatusCode.BadRequest, replaced.StatusCode);
        Assert.Contains("expired", await replaced.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await second.PostAsync("api/account/signin", Json(authenticator.GetAssertion(secondOptions)))).StatusCode);
    }

    [Fact]
    public async Task ChallengesExpire()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient _, TestAuthenticator? authenticator) = await SignUpAsync(app, "alice");

        using HttpClient browser = app.CreateBrowserClient();
        string options = await PostAsync(browser, "api/account/signin/options", new { userName = "alice" });
        app.Clock.Advance(TimeSpan.FromMinutes(6));

        HttpResponseMessage signIn = await browser.PostAsync("api/account/signin", Json(authenticator.GetAssertion(options)));

        Assert.Equal(HttpStatusCode.BadRequest, signIn.StatusCode);
        Assert.Contains("expired", await signIn.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OnlyAccountsCanSignIn()
    {
        NuDepotTestApplication app = CreateApp();
        using HttpClient browser = app.CreateBrowserClient();
        await PostAsync(browser, "api/account/signup", new { userName = "never-finished" });

        HttpResponseMessage unknown = await browser.PostAsync("api/account/signin/options", JsonContent.Create(new { userName = "nobody" }));
        HttpResponseMessage unfinished = await browser.PostAsync("api/account/signin/options", JsonContent.Create(new { userName = "never-finished" }));

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unfinished.StatusCode);
        Assert.Contains("no account", await unfinished.Content.ReadAsStringAsync());

        // An unfinished sign-up isn't an account, so the next one is still the first.
        Assert.True((await GetAccountAsync(browser)).GetProperty("isFirstUser").GetBoolean());
    }

    [Fact]
    public async Task UnknownPasskeysCantSignIn()
    {
        NuDepotTestApplication app = CreateApp();
        await SignUpAsync(app, "alice");

        // A passkey made for a sign-up that was never finished.
        using TestAuthenticator stranger = new();
        using HttpClient browser = app.CreateBrowserClient();
        stranger.CreateCredential(await PostAsync(browser, "api/account/signup", new { userName = "never-finished" }));

        string options = await PostAsync(browser, "api/account/signin/options", new { userName = "alice" });
        HttpResponseMessage signIn = await browser.PostAsync("api/account/signin", Json(stranger.GetAssertion(options)));

        Assert.Equal(HttpStatusCode.BadRequest, signIn.StatusCode);
        Assert.Contains("isn't registered", await signIn.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnUnfinishedSignUpIsReplacedByTheNext()
    {
        NuDepotTestApplication app = CreateApp();
        using HttpClient first = app.CreateBrowserClient();
        using HttpClient second = app.CreateBrowserClient();
        using TestAuthenticator firstAuthenticator = new();
        using TestAuthenticator secondAuthenticator = new();

        string firstOptions = await PostAsync(first, "api/account/signup", new { userName = "bob" });
        string secondOptions = await PostAsync(second, "api/account/signup", new { userName = "bob" });

        HttpResponseMessage replaced = await first.PostAsync("api/account/passkeys?userName=bob", Json(firstAuthenticator.CreateCredential(firstOptions)));
        HttpResponseMessage created = await second.PostAsync("api/account/passkeys?userName=bob", Json(secondAuthenticator.CreateCredential(secondOptions)));

        Assert.Equal(HttpStatusCode.BadRequest, replaced.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);

        // Once the account is created, the user name is taken.
        HttpResponseMessage third = await first.PostAsync("api/account/signup", JsonContent.Create(new { userName = "bob" }));
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
    }

    [Fact]
    public async Task RoleChangesReachSignedInUsersStraightAway()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");
        Assert.Equal(HttpStatusCode.Created, (await alice.PutAsync("api/v2/package", Package("Admin.Package"))).StatusCode);

        await SetRoleAsync(app, "alice", FeedRole.ReadOnly);

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PutAsync("api/v2/package", Package("ReadOnly.Package"))).StatusCode);
    }

    [Fact]
    public async Task ACopiedCookieRevokesTheSession()
    {
        NuDepotTestApplication app = CreateApp();
        CookieContainer cookies = new();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice", cookies);

        using HttpClient thief = app.CreateBrowserClient();
        thief.DefaultRequestHeaders.Add("Cookie", cookies.GetCookieHeader(alice.BaseAddress!));

        // Alice keeps using the feed, which replaces her cookie.
        Assert.True((await GetAccountAsync(alice)).GetProperty("signedIn").GetBoolean());
        app.Clock.Advance(TimeSpan.FromMinutes(2));

        // The copy is too old to have been sent with Alice's requests, so it was stolen.
        Assert.False((await GetAccountAsync(thief)).GetProperty("signedIn").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await thief.GetAsync("api/account/apikeys")).StatusCode);
        Assert.False((await GetAccountAsync(alice)).GetProperty("signedIn").GetBoolean());
    }

    [Fact]
    public async Task RequestsSentTogetherShareTheCookie()
    {
        NuDepotTestApplication app = CreateApp();
        CookieContainer cookies = new();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice", cookies);

        // Like a page's requests sent at once, or the website's requests to the feed for the browser.
        using HttpClient sentTogether = app.CreateClient();
        sentTogether.DefaultRequestHeaders.Add("Cookie", cookies.GetCookieHeader(alice.BaseAddress!));

        Assert.True((await GetAccountAsync(alice)).GetProperty("signedIn").GetBoolean());
        Assert.True((await GetAccountAsync(sentTogether)).GetProperty("signedIn").GetBoolean());
        Assert.True((await GetAccountAsync(alice)).GetProperty("signedIn").GetBoolean());
    }

    [Fact]
    public async Task SignOutEndsTheSessionForCopiesOfTheCookie()
    {
        NuDepotTestApplication app = CreateApp();
        CookieContainer cookies = new();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice", cookies);

        using HttpClient copy = app.CreateClient();
        copy.DefaultRequestHeaders.Add("Cookie", cookies.GetCookieHeader(alice.BaseAddress!));

        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsync("api/account/signout", null)).StatusCode);

        Assert.False((await GetAccountAsync(copy)).GetProperty("signedIn").GetBoolean());
    }

    [Fact]
    public async Task EachRequestKeepsTheUserSignedInForAnotherYear()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");

        for (int i = 0; i < 3; i++)
        {
            app.Clock.Advance(TimeSpan.FromDays(300));
            HttpResponseMessage response = await alice.GetAsync("api/account");

            Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("signedIn").GetBoolean());
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("NuDepot.User=", StringComparison.Ordinal));
        }

        app.Clock.Advance(TimeSpan.FromDays(366));
        Assert.False((await GetAccountAsync(alice)).GetProperty("signedIn").GetBoolean());
    }

    [Theory]
    [InlineData("alice", HttpStatusCode.Conflict)]
    [InlineData("ALICE", HttpStatusCode.Conflict)]
    [InlineData("not valid!", HttpStatusCode.BadRequest)]
    [InlineData("", HttpStatusCode.BadRequest)]
    public async Task UserNamesMustBeValidAndUnique(string userName, HttpStatusCode expected)
    {
        NuDepotTestApplication app = CreateApp();
        await SignUpAsync(app, "alice");

        using HttpClient browser = app.CreateBrowserClient();
        HttpResponseMessage response = await browser.PostAsync("api/account/signup", JsonContent.Create(new { userName }));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task OnlyTheFirstAccountCanSignUpWhenSignUpIsOff()
    {
        NuDepotTestApplication app = CreateApp(new() { { "Authorization:Users:AllowSignUp", "false" } });

        await SignUpAsync(app, "admin");

        using HttpClient browser = app.CreateBrowserClient();
        HttpResponseMessage response = await browser.PostAsync("api/account/signup", JsonContent.Create(new { userName = "bob" }));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False((await GetAccountAsync(browser)).GetProperty("canSignUp").GetBoolean());
    }

    [Theory]
    [InlineData("alice", HttpStatusCode.Created)]
    [InlineData("bob", HttpStatusCode.Forbidden)]
    public async Task SignedInUsersHaveTheirRoleOnTheFeed(string userName, HttpStatusCode expected)
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");
        (HttpClient? bob, TestAuthenticator _) = await SignUpAsync(app, "bob");

        HttpClient browser = userName == "alice" ? alice : bob;
        HttpResponseMessage response = await browser.PutAsync("api/v2/package", Package("Signed.In.Package"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task SignedInUsersReadAFeedClosedToEveryoneElse()
    {
        NuDepotTestApplication app = CreateApp(new() { { "Authorization:AnonymousRole", "None" } });
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");

        using HttpClient anonymous = app.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("v3/index.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("v3/index.json")).StatusCode);
    }

    [Fact]
    public async Task UserApiKeysActAsTheirUser()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");
        (HttpClient? bob, TestAuthenticator _) = await SignUpAsync(app, "bob");

        string aliceKey = await CreateApiKeyAsync(alice, "Build server");
        string bobKey = await CreateApiKeyAsync(bob, "Laptop");
        Assert.StartsWith("nd_", aliceKey);

        using HttpClient client = app.CreateClient();

        // Like the push command's -k option, and like nuget.config's package source credentials.
        Assert.Equal(HttpStatusCode.Created, (await SendWithKeyAsync(client, aliceKey, "Header.Package", basic: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendWithKeyAsync(client, aliceKey, "Basic.Package", basic: true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendWithKeyAsync(client, bobKey, "ReadOnly.Package", basic: false)).StatusCode);
    }

    [Fact]
    public async Task RevokedApiKeysStopWorking()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");
        string key = await CreateApiKeyAsync(alice, "Build server");

        JsonElement apiKeys = await alice.GetFromJsonAsync<JsonElement>("api/account/apikeys");
        JsonElement apiKey = Assert.Single(apiKeys.EnumerateArray());
        Assert.Equal(key[..10], apiKey.GetProperty("prefix").GetString());
        Assert.False(apiKey.TryGetProperty("key", out _));

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"api/account/apikeys/{apiKey.GetProperty("id").GetInt32()}")).StatusCode);

        using HttpClient client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendWithKeyAsync(client, key, "Revoked.Package", basic: false)).StatusCode);
    }

    [Fact]
    public async Task UsersCanAddPasskeysButNotRemoveTheirLast()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");

        JsonElement passkeys = await alice.GetFromJsonAsync<JsonElement>("api/account/passkeys");
        int first = Assert
            .Single(passkeys.EnumerateArray())
            .GetProperty("id")
            .GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await alice.DeleteAsync($"api/account/passkeys/{first}")).StatusCode);

        using TestAuthenticator phone = new();
        string options = await PostAsync(alice, "api/account/passkeys/options", new { passkeyName = "Phone" });
        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsync("api/account/passkeys", Json(phone.CreateCredential(options)))).StatusCode);

        passkeys = await alice.GetFromJsonAsync<JsonElement>("api/account/passkeys");
        Assert.Equal(2, passkeys.GetArrayLength());
        Assert.Equal("Phone", passkeys[1].GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"api/account/passkeys/{first}")).StatusCode);

        // The new passkey signs in.
        using HttpClient browser = app.CreateBrowserClient();
        string signInOptions = await PostAsync(browser, "api/account/signin/options", new { userName = "alice" });
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("api/account/signin", Json(phone.GetAssertion(signInOptions)))).StatusCode);
    }

    [Fact]
    public async Task AccountsNeedASignInCookie()
    {
        NuDepotTestApplication app = CreateApp(new() { { "ApiKey", "admin-key" } });
        using HttpClient client = app.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, "api/account/apikeys");
        request.Headers.Add("X-NuGet-ApiKey", "admin-key");
        HttpResponseMessage response = await client.SendAsync(request);

        // API keys can't manage accounts, and the browser isn't asked for Basic credentials.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task SignOutEndsTheSession()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");

        HttpResponseMessage signOut = await alice.PostAsync("api/account/signout", null);
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);

        Assert.False((await GetAccountAsync(alice)).GetProperty("signedIn").GetBoolean());
    }

    [Fact]
    public void UsersWithoutUsePasskeysStopStartup()
    {
        NuDepotTestApplication app = new(Output, configuration: UsersEnabled(), usePasskeys: false);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => app.Services);

        Assert.Contains(exception.Failures, f => f.Contains("Call UsePasskeys()"));
    }

    [Fact]
    public void UsersWithoutUseApiKeysStopStartup()
    {
        NuDepotTestApplication app = new(Output, configuration: UsersEnabled(), useApiKeys: false);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => app.Services);

        Assert.Contains(exception.Failures, f => f.Contains("who create their own API keys, but doesn't accept API keys"));
    }

    [Fact]
    public async Task AccountsArentMappedWithoutUsers()
    {
        NuDepotTestApplication app = new(Output);
        Disposables.Add(app);
        using HttpClient client = app.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("api/account");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static Dictionary<string, string> UsersEnabled(Dictionary<string, string>? configuration = null)
    {
        Dictionary<string, string> settings = new(configuration ?? [])
        {
            ["Authorization:Users:Enabled"] = "true"
        };
        return settings;
    }

    private NuDepotTestApplication CreateApp(Dictionary<string, string>? configuration = null)
    {
        NuDepotTestApplication app = new(Output, configuration: UsersEnabled(configuration));
        Disposables.Add(app);
        return app;
    }

    /// <summary>
    /// Sign up the way the website does, returning a client that's signed in as the new user.
    /// </summary>
    private async Task<(HttpClient Browser, TestAuthenticator Authenticator)> SignUpAsync(NuDepotTestApplication app, string userName, CookieContainer? cookies = null)
    {
        HttpClient browser = app.CreateBrowserClient(cookies);
        TestAuthenticator authenticator = new();
        Disposables.Add(browser);
        Disposables.Add(authenticator);

        string options = await PostAsync(browser, "api/account/signup", new { userName, passkeyName = "Laptop" });
        HttpResponseMessage response = await browser.PostAsync($"api/account/passkeys?userName={userName}", Json(authenticator.CreateCredential(options)));
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        return (browser, authenticator);
    }

    private static async Task<string> PostAsync(HttpClient client, string url, object body)
    {
        HttpResponseMessage response = await client.PostAsync(url, JsonContent.Create(body));
        string content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, content);

        return content;
    }

    private static async Task SetRoleAsync(NuDepotTestApplication app, string userName, FeedRole role)
    {
        using IServiceScope scope = app.Services.CreateScope();
        NuDepotContext context = scope.ServiceProvider.GetRequiredService<NuDepotContext>();

        User user = await context.Users.SingleAsync(u => u.UserName == userName);
        user.Role = role;
        await context.SaveChangesAsync();
    }

    private static async Task<JsonElement> GetAccountAsync(HttpClient client)
        => await client.GetFromJsonAsync<JsonElement>("api/account");

    private static async Task<string> CreateApiKeyAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsync("api/account/apikeys", JsonContent.Create(new { name }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        JsonElement apiKey = await response.Content.ReadFromJsonAsync<JsonElement>();
        return apiKey
            .GetProperty("key")
            .GetString()!;
    }

    private static Task<HttpResponseMessage> SendWithKeyAsync(HttpClient client, string key, string packageId, bool basic)
    {
        HttpRequestMessage request = new(HttpMethod.Put, "api/v2/package") { Content = Package(packageId) };
        if (basic)
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("me:" + key)));
        else
            request.Headers.Add("X-NuGet-ApiKey", key);

        return client.SendAsync(request);
    }

    private static MultipartFormDataContent Package(string id) => new()
    {
        { new StreamContent(TestPackages.Create(id, "1.0.0")), "package", "package.nupkg" },
    };

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
