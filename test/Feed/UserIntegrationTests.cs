using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Tests.Support;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Tests users who sign up and sign in with passkeys, and their API keys.
/// </summary>
public sealed class UserIntegrationTests(ITestOutputHelper output) : IDisposable
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
        HttpResponseMessage signIn = await browser.PostAsync("api/account/signin", Json(authenticator.GetAssertion(options)), TestContext.Current.CancellationToken);

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

        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("api/account/signin", Json(assertion), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("api/account/signin", Json(assertion), TestContext.Current.CancellationToken)).StatusCode);
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

        HttpResponseMessage replaced = await first.PostAsync("api/account/signin", Json(authenticator.GetAssertion(firstOptions)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, replaced.StatusCode);
        Assert.Contains("expired", await replaced.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.NoContent, (await second.PostAsync("api/account/signin", Json(authenticator.GetAssertion(secondOptions)), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("origin")]
    public async Task SignInsThatFailVerificationDontSignIn(string forgery)
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient _, TestAuthenticator? authenticator) = await SignUpAsync(app, "alice");
        using HttpClient browser = app.CreateBrowserClient();
        string options = await PostAsync(browser, "api/account/signin/options", new { userName = "alice" });

        // An altered signature, or a response a phishing site got for its own address.
        JsonNode assertion = JsonNode.Parse(authenticator.GetAssertion(options))!;
        if (forgery == "signature")
        {
            byte[] signature = WebEncoders.Base64UrlDecode(assertion["response"]!["signature"]!.GetValue<string>());
            signature[^1] ^= 0xFF;
            assertion["response"]!["signature"] = WebEncoders.Base64UrlEncode(signature);
        }
        else
        {
            JsonNode clientData = JsonNode.Parse(WebEncoders.Base64UrlDecode(assertion["response"]!["clientDataJSON"]!.GetValue<string>()))!;
            clientData["origin"] = "http://phishing.example";
            assertion["response"]!["clientDataJSON"] = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(clientData.ToJsonString()));
        }

        HttpResponseMessage signIn = await browser.PostAsync("api/account/signin", Json(assertion.ToJsonString()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, signIn.StatusCode);
        Assert.Contains("couldn't be verified", await signIn.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False((await GetAccountAsync(browser)).GetProperty("signedIn").GetBoolean());

        // The challenge is used up, but the next one works.
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("api/account/signin", Json(authenticator.GetAssertion(options)), TestContext.Current.CancellationToken)).StatusCode);
        string retry = await PostAsync(browser, "api/account/signin/options", new { userName = "alice" });
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("api/account/signin", Json(authenticator.GetAssertion(retry)), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task PasskeysMadeForAnotherSiteDontCreateAccounts()
    {
        NuDepotTestApplication app = CreateApp();
        using HttpClient browser = app.CreateBrowserClient();
        using TestAuthenticator phishing = new("http://phishing.example");

        string options = await PostAsync(browser, "api/account/signup", new { userName = "mallory" });
        HttpResponseMessage created = await browser.PostAsync("api/account/passkeys?userName=mallory", Json(phishing.CreateCredential(options)), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Contains("couldn't be verified", await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False((await GetAccountAsync(browser)).GetProperty("signedIn").GetBoolean());
        Assert.True((await GetAccountAsync(browser)).GetProperty("isFirstUser").GetBoolean());
    }

    // Every account endpoint needs the sign-in cookie, except signing up and signing in.
    [Theory]
    [InlineData("GET", "api/account/sessions")]
    [InlineData("DELETE", "api/account/sessions/1")]
    [InlineData("GET", "api/account/passkeys")]
    [InlineData("POST", "api/account/passkeys/options")]
    [InlineData("POST", "api/account/passkeys")]
    [InlineData("DELETE", "api/account/passkeys/1")]
    [InlineData("GET", "api/account/apikeys")]
    [InlineData("POST", "api/account/apikeys")]
    [InlineData("DELETE", "api/account/apikeys/1")]
    public async Task AccountsNeedASignedInUser(string method, string url)
    {
        NuDepotTestApplication app = CreateApp();
        await SignUpAsync(app, "alice");
        using HttpClient anonymous = app.CreateBrowserClient();

        using HttpRequestMessage request = new(new HttpMethod(method), url) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        using HttpResponseMessage response = await anonymous.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChallengesExpire()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient _, TestAuthenticator? authenticator) = await SignUpAsync(app, "alice");

        using HttpClient browser = app.CreateBrowserClient();
        string options = await PostAsync(browser, "api/account/signin/options", new { userName = "alice" });
        app.Clock.Advance(TimeSpan.FromMinutes(6));

        HttpResponseMessage signIn = await browser.PostAsync("api/account/signin", Json(authenticator.GetAssertion(options)), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, signIn.StatusCode);
        Assert.Contains("expired", await signIn.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OnlyAccountsCanSignIn()
    {
        NuDepotTestApplication app = CreateApp();
        using HttpClient browser = app.CreateBrowserClient();
        await PostAsync(browser, "api/account/signup", new { userName = "never-finished" });

        HttpResponseMessage unknown = await browser.PostAsync("api/account/signin/options", JsonContent.Create(new { userName = "nobody" }), TestContext.Current.CancellationToken);
        HttpResponseMessage unfinished = await browser.PostAsync("api/account/signin/options", JsonContent.Create(new { userName = "never-finished" }), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unfinished.StatusCode);
        Assert.Contains("no account", await unfinished.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

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
        HttpResponseMessage signIn = await browser.PostAsync("api/account/signin", Json(stranger.GetAssertion(options)), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, signIn.StatusCode);
        Assert.Contains("isn't registered", await signIn.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
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

        HttpResponseMessage replaced = await first.PostAsync("api/account/passkeys?userName=bob", Json(firstAuthenticator.CreateCredential(firstOptions)), TestContext.Current.CancellationToken);
        HttpResponseMessage created = await second.PostAsync("api/account/passkeys?userName=bob", Json(secondAuthenticator.CreateCredential(secondOptions)), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, replaced.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);

        // Once the account is created, the user name is taken.
        HttpResponseMessage third = await first.PostAsync("api/account/signup", JsonContent.Create(new { userName = "bob" }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
    }

    [Fact]
    public async Task RoleChangesReachSignedInUsersStraightAway()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");
        Assert.Equal(HttpStatusCode.Created, (await alice.PutAsync("api/v2/package", Package("Admin.Package"), TestContext.Current.CancellationToken)).StatusCode);

        await SetRoleAsync(app, "alice", FeedRole.ReadOnly);

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PutAsync("api/v2/package", Package("ReadOnly.Package"), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ACopiedCookieRevokesTheSession()
    {
        NuDepotTestApplication app = CreateApp();
        CookieContainer cookies = new();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice", cookies);

        using HttpClient thief = app.CreateBrowserClient();
        thief.DefaultRequestHeaders.Add("Cookie", cookies.GetCookieHeader(alice.BaseAddress!));

        // Alice keeps using the feed, which replaces her cookie, and her browser uses the new one.
        app.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.True((await GetAccountAsync(alice)).GetProperty("signedIn").GetBoolean());
        Assert.True((await GetAccountAsync(alice)).GetProperty("signedIn").GetBoolean());
        app.Clock.Advance(TimeSpan.FromMinutes(2));

        // The copy is too old to have been sent with Alice's requests, so it was stolen.
        Assert.False((await GetAccountAsync(thief)).GetProperty("signedIn").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await thief.GetAsync("api/account/apikeys", TestContext.Current.CancellationToken)).StatusCode);
        Assert.False((await GetAccountAsync(alice)).GetProperty("signedIn").GetBoolean());
    }

    [Fact]
    public async Task TheCookieIsReplacedEveryFifteenMinutes()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");

        app.Clock.Advance(TimeSpan.FromMinutes(14));
        using (HttpResponseMessage response = await alice.GetAsync("api/account", TestContext.Current.CancellationToken))
            Assert.False(response.Headers.Contains("Set-Cookie"));

        app.Clock.Advance(TimeSpan.FromMinutes(1));
        using (HttpResponseMessage response = await alice.GetAsync("api/account", TestContext.Current.CancellationToken))
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("NuDepot.User=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACookieThatNeverArrivedIsReplacedAgain()
    {
        NuDepotTestApplication app = CreateApp();
        CookieContainer cookies = new();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice", cookies);
        string original = cookies.GetCookieHeader(alice.BaseAddress!);

        // The response with the new cookie is lost, like when Alice leaves a page while it loads.
        app.Clock.Advance(TimeSpan.FromMinutes(16));
        using (HttpClient lost = app.CreateClient())
        {
            lost.DefaultRequestHeaders.Add("Cookie", original);
            using HttpResponseMessage response = await lost.GetAsync("api/account", TestContext.Current.CancellationToken);
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("NuDepot.User=", StringComparison.Ordinal));
        }

        // Her browser still has the old cookie, long after it was replaced, and gets another.
        app.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await GetAccountAsync(alice)).GetProperty("signedIn").GetBoolean());
        Assert.NotEqual(original, cookies.GetCookieHeader(alice.BaseAddress!));
        Assert.True((await GetAccountAsync(alice)).GetProperty("signedIn").GetBoolean());

        // Once her browser used the new one, the old cookie is a copy.
        app.Clock.Advance(TimeSpan.FromMinutes(2));
        using HttpClient copy = app.CreateClient();
        copy.DefaultRequestHeaders.Add("Cookie", original);
        Assert.False((await GetAccountAsync(copy)).GetProperty("signedIn").GetBoolean());
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

        app.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.True((await GetAccountAsync(alice)).GetProperty("signedIn").GetBoolean());
        Assert.True((await GetAccountAsync(sentTogether)).GetProperty("signedIn").GetBoolean());
        Assert.True((await GetAccountAsync(alice)).GetProperty("signedIn").GetBoolean());
    }

    [Theory]
    [InlineData("Sec-Fetch-Site", "same-site", HttpStatusCode.Unauthorized)]
    [InlineData("Sec-Fetch-Site", "cross-site", HttpStatusCode.Unauthorized)]
    [InlineData("Origin", "http://localhost:8080", HttpStatusCode.Unauthorized)]
    [InlineData("Origin", "null", HttpStatusCode.Unauthorized)]
    [InlineData("Sec-Fetch-Site", "same-origin", HttpStatusCode.Created)]
    [InlineData("Origin", "http://localhost", HttpStatusCode.Created)]
    public async Task OnlyTheGallerysPagesCanChangeThingsAsTheUser(string header, string value, HttpStatusCode expected)
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");

        // Like a form on another port or subdomain of the same host, which the browser sends the cookie with.
        using HttpRequestMessage request = new(HttpMethod.Post, "api/account/apikeys")
        {
            Content = new StringContent(@"{""name"":""Planted""}", Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add(header, value);
        using HttpResponseMessage response = await alice.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);

        // The user stays signed in, and can still read.
        using HttpRequestMessage read = new(HttpMethod.Get, "api/account");
        read.Headers.Add("Sec-Fetch-Site", "cross-site");
        Assert.True((await (await alice.SendAsync(read, TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("signedIn").GetBoolean());
    }

    [Fact]
    public async Task SignOutEndsTheSessionForCopiesOfTheCookie()
    {
        NuDepotTestApplication app = CreateApp();
        CookieContainer cookies = new();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice", cookies);

        using HttpClient copy = app.CreateClient();
        copy.DefaultRequestHeaders.Add("Cookie", cookies.GetCookieHeader(alice.BaseAddress!));

        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsync("api/account/signout", null, TestContext.Current.CancellationToken)).StatusCode);

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
            HttpResponseMessage response = await alice.GetAsync("api/account", TestContext.Current.CancellationToken);

            Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("signedIn").GetBoolean());
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("NuDepot.User=", StringComparison.Ordinal));
        }

        app.Clock.Advance(TimeSpan.FromDays(366));
        Assert.False((await GetAccountAsync(alice)).GetProperty("signedIn").GetBoolean());
    }

    [Theory]
    [InlineData("alice", HttpStatusCode.Conflict)]
    [InlineData("ALICE", HttpStatusCode.Conflict)]
    [InlineData("not valid!", HttpStatusCode.BadRequest)]
    // Role names name the feed's API keys in credentials.
    [InlineData("ReadWrite", HttpStatusCode.BadRequest)]
    [InlineData("admin", HttpStatusCode.BadRequest)]
    [InlineData("", HttpStatusCode.BadRequest)]
    public async Task UserNamesMustBeValidAndUnique(string userName, HttpStatusCode expected)
    {
        NuDepotTestApplication app = CreateApp();
        await SignUpAsync(app, "alice");

        using HttpClient browser = app.CreateBrowserClient();
        HttpResponseMessage response = await browser.PostAsync("api/account/signup", JsonContent.Create(new { userName }), TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task OnlyTheFirstAccountCanSignUpWhenSignUpIsOff()
    {
        NuDepotTestApplication app = CreateApp(new() { { "Authorization:Users:AllowSignUp", "false" } });

        await SignUpAsync(app, "owner");

        using HttpClient browser = app.CreateBrowserClient();
        HttpResponseMessage response = await browser.PostAsync("api/account/signup", JsonContent.Create(new { userName = "bob" }), TestContext.Current.CancellationToken);
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
        HttpResponseMessage response = await browser.PutAsync("api/v2/package", Package("Signed.In.Package"), TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task SignedInUsersReadAFeedClosedToEveryoneElse()
    {
        NuDepotTestApplication app = CreateApp(new() { { "Authorization:ApiKeys:ReadOnly", "read-only-key" } });
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");

        using HttpClient anonymous = app.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("v3/index.json", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("v3/index.json", TestContext.Current.CancellationToken)).StatusCode);
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

        // Like nuget.config's package source credentials, with the user's name.
        Assert.Equal(HttpStatusCode.Created, (await SendWithKeyAsync(client, aliceKey, "Basic.Package", userName: "alice")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendWithKeyAsync(client, aliceKey, "Any.Case.Package", userName: "ALICE")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendWithKeyAsync(client, bobKey, "ReadOnly.Package", userName: "bob")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bob")]
    [InlineData("ReadWrite")]
    public async Task UserApiKeysOnlyWorkWithTheirUsersName(string? userName)
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");
        string aliceKey = await CreateApiKeyAsync(alice, "Build server");

        // Without a user name, the key is in the X-NuGet-ApiKey header, like the push command's -k option.
        using HttpClient client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendWithKeyAsync(client, aliceKey, "Rejected.Package", userName)).StatusCode);
    }

    [Fact]
    public async Task WithUsersAnonymousRequestsMayOnlyRead()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");
        using HttpClient anonymous = app.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("v3/index.json", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsync("api/v2/package", Package("Anonymous.Package"), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await alice.PutAsync("api/v2/package", Package("Alice.Package"), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task AnonymousReadsCanBeTurnedOff()
    {
        NuDepotTestApplication app = CreateApp(new() { { "Authorization:Users:AnonymousReads", "false" } });
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");
        using HttpClient anonymous = app.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("v3/index.json", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("v3/index.json", TestContext.Current.CancellationToken)).StatusCode);

        // The gallery's sign-in still works.
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("api/account", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task RevokedApiKeysStopWorking()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");
        string key = await CreateApiKeyAsync(alice, "Build server");

        JsonElement apiKeys = await alice.GetFromJsonAsync<JsonElement>("api/account/apikeys", cancellationToken: TestContext.Current.CancellationToken);
        JsonElement apiKey = Assert.Single(apiKeys.EnumerateArray());
        Assert.Equal(key[..10], apiKey.GetProperty("prefix").GetString());
        Assert.False(apiKey.TryGetProperty("key", out _));

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"api/account/apikeys/{apiKey.GetProperty("id").GetInt32()}", TestContext.Current.CancellationToken)).StatusCode);

        using HttpClient client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendWithKeyAsync(client, key, "Revoked.Package", userName: "alice")).StatusCode);
    }

    [Fact]
    public async Task UsersCanAddPasskeysButNotRemoveTheirLast()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");

        JsonElement passkeys = await alice.GetFromJsonAsync<JsonElement>("api/account/passkeys", cancellationToken: TestContext.Current.CancellationToken);
        int first = Assert
            .Single(passkeys.EnumerateArray())
            .GetProperty("id")
            .GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await alice.DeleteAsync($"api/account/passkeys/{first}", TestContext.Current.CancellationToken)).StatusCode);

        using TestAuthenticator phone = new();
        string options = await PostAsync(alice, "api/account/passkeys/options", new { passkeyName = "Phone" });
        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsync("api/account/passkeys", Json(phone.CreateCredential(options)), TestContext.Current.CancellationToken)).StatusCode);

        passkeys = await alice.GetFromJsonAsync<JsonElement>("api/account/passkeys", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, passkeys.GetArrayLength());
        Assert.Equal("Phone", passkeys[1].GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"api/account/passkeys/{first}", TestContext.Current.CancellationToken)).StatusCode);

        // The new passkey signs in.
        using HttpClient browser = app.CreateBrowserClient();
        string signInOptions = await PostAsync(browser, "api/account/signin/options", new { userName = "alice" });
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("api/account/signin", Json(phone.GetAssertion(signInOptions)), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task AccountsNeedASignInCookie()
    {
        NuDepotTestApplication app = CreateApp(new() { { "Authorization:ApiKeys:Admin", "admin-key" } });
        using HttpClient client = app.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, "api/account/apikeys");
        request.Headers.Add("X-NuGet-ApiKey", "admin-key");
        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // API keys can't manage accounts, and the browser isn't asked for Basic credentials.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task SignOutEndsTheSession()
    {
        NuDepotTestApplication app = CreateApp();
        (HttpClient? alice, TestAuthenticator _) = await SignUpAsync(app, "alice");

        HttpResponseMessage signOut = await alice.PostAsync("api/account/signout", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);

        Assert.False((await GetAccountAsync(alice)).GetProperty("signedIn").GetBoolean());
    }

    [Fact]
    public async Task AccountsArentMappedWithoutUsers()
    {
        NuDepotTestApplication app = new(Output);
        Disposables.Add(app);
        using HttpClient client = app.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("api/account", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AccountChangesAreLoggedWithWhoMadeThem()
    {
        LogRecorder logs = new();
        NuDepotTestApplication app = CreateApp(logs: logs);
        (HttpClient? alice, TestAuthenticator? authenticator) = await SignUpAsync(app, "alice");
        await SignUpAsync(app, "bob");

        string key = await CreateApiKeyAsync(alice, "Build server");
        using HttpClient client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Created, (await SendWithKeyAsync(client, key, "Logged.Package", userName: "alice")).StatusCode);
        int apiKeyId = (await alice.GetFromJsonAsync<JsonElement>("api/account/apikeys", cancellationToken: TestContext.Current.CancellationToken))[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"api/account/apikeys/{apiKeyId}", TestContext.Current.CancellationToken)).StatusCode);

        int bobId = (await alice.GetFromJsonAsync<JsonElement>("api/admin/users", cancellationToken: TestContext.Current.CancellationToken))[1].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"api/admin/users/{bobId}/sessions", TestContext.Current.CancellationToken)).StatusCode);

        // A page from another site, and a sign-in that's sent again.
        using HttpRequestMessage crossSite = new(HttpMethod.Post, "api/account/apikeys") { Content = JsonContent.Create(new { name = "Stolen" }) };
        crossSite.Headers.Add("Origin", "http://evil.example");
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.SendAsync(crossSite, TestContext.Current.CancellationToken)).StatusCode);
        using HttpClient browser = app.CreateBrowserClient();
        string options = await PostAsync(browser, "api/account/signin/options", new { userName = "alice" });
        string assertion = authenticator.GetAssertion(options);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("api/account/signin", Json(assertion), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("api/account/signin", Json(assertion), TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsync("api/account/signout", null, TestContext.Current.CancellationToken)).StatusCode);

        IReadOnlyList<string> information = logs.Messages(LogLevel.Information);
        IReadOnlyList<string> warnings = logs.Messages(LogLevel.Warning);
        Assert.Contains(information, m => m.StartsWith("An anonymous client (", StringComparison.Ordinal) && m.EndsWith("created the user alice, with the Admin role", StringComparison.Ordinal));
        Assert.Contains(information, m => m.StartsWith("alice signed in from ", StringComparison.Ordinal));
        Assert.Contains(information, m => m.StartsWith("alice (", StringComparison.Ordinal) && m.EndsWith($"created the API key Build server, which starts with {key[..10]}", StringComparison.Ordinal));
        Assert.Contains(information, m => m.StartsWith("alice's API key (", StringComparison.Ordinal) && m.EndsWith("pushed package Logged.Package 1.0.0", StringComparison.Ordinal));
        Assert.Contains(information, m => m.StartsWith("alice (", StringComparison.Ordinal) && m.EndsWith($"revoked their API key {apiKeyId}", StringComparison.Ordinal));
        Assert.Contains(warnings, m => m.StartsWith("alice (", StringComparison.Ordinal) && m.EndsWith("signed bob out of every browser", StringComparison.Ordinal));
        Assert.Contains(warnings, m => m.StartsWith("Ignored the sign-in cookie of POST /api/account/apikeys from ", StringComparison.Ordinal) && m.EndsWith("http://evil.example, sent it", StringComparison.Ordinal));
        Assert.Contains(warnings, m => m.StartsWith("A passkey sign-in from ", StringComparison.Ordinal));
        Assert.Contains(information, m => m.StartsWith("alice (", StringComparison.Ordinal) && m.EndsWith("signed out", StringComparison.Ordinal));

        // Keys aren't logged.
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(key, StringComparison.Ordinal));
    }

    private NuDepotTestApplication CreateApp(Dictionary<string, string>? configuration = null, LogRecorder? logs = null)
    {
        NuDepotTestApplication app = new(
            Output,
            configuration: configuration,
            users: true,
            configureServices: logs == null ? null : services => services.AddSingleton<ILoggerProvider>(logs));
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
        NuDepotDbContext context = scope.ServiceProvider.GetRequiredService<NuDepotDbContext>();

        await context.Users
            .Where(u => u.UserName == userName)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.Role, role));
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

    // With a user name, the key is the password of credentials; without one, it's in the X-NuGet-ApiKey header.
    private static Task<HttpResponseMessage> SendWithKeyAsync(HttpClient client, string key, string packageId, string? userName)
    {
        HttpRequestMessage request = new(HttpMethod.Put, "api/v2/package") { Content = Package(packageId) };
        if (userName != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(userName + ":" + key)));
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
