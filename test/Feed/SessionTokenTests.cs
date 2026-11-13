using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NuDepot.Core;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Tests.Support;
using NuDepot.Feed.Users;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Tests the sign-in cookie's format: a JWT, signed with a key the feed keeps in its database.
/// </summary>
public sealed class SessionTokenTests(ITestOutputHelper output) : IDisposable
{
    private const string CookieName = "NuDepot.User";

    private ITestOutputHelper Output { get; } = output;
    private List<IDisposable> Disposables { get; } = [];

    public void Dispose()
    {
        foreach (IDisposable disposable in Disposables)
            disposable.Dispose();
    }

    [Fact]
    public async Task TheCookieIsASignedJwtOfTheSession()
    {
        NuDepotTestApplication app = CreateApp();
        CookieContainer cookies = new();
        await SignUpAsync(app, "alice", cookies);

        string[] parts = GetCookie(cookies).Split('.');

        Assert.Equal(3, parts.Length);
        JsonElement header = Decode(parts[0]);
        JsonElement payload = Decode(parts[1]);
        Assert.Equal("ES256", header.GetProperty("alg").GetString());
        Assert.Equal(32, header.GetProperty("kid").GetString()!.Length);
        Assert.Equal("NuDepot", payload.GetProperty("iss").GetString());
        Assert.Equal("NuDepot", payload.GetProperty("aud").GetString());
        Assert.Equal("alice", payload.GetProperty("name").GetString());
        Assert.Equal("Admin", payload.GetProperty("role").GetString());
        Assert.False(string.IsNullOrEmpty(payload.GetProperty("sub").GetString()));
        Assert.False(string.IsNullOrEmpty(payload.GetProperty("sid").GetString()));
        Assert.False(string.IsNullOrEmpty(payload.GetProperty("sit").GetString()));

        // A year after it was issued, like the session.
        long lifetime = payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64();
        Assert.Equal((long)UserService.SessionLifetime.TotalSeconds, lifetime);
    }

    [Fact]
    public async Task TheTokenIsReplacedEveryFifteenMinutes()
    {
        NuDepotTestApplication app = CreateApp();
        CookieContainer cookies = new();
        HttpClient browser = await SignUpAsync(app, "alice", cookies);
        string first = Decode(GetCookie(cookies).Split('.')[1]).GetProperty("sit").GetString()!;

        await browser.GetAsync("api/account");
        Assert.Equal(first, Decode(GetCookie(cookies).Split('.')[1]).GetProperty("sit").GetString());

        app.Clock.Advance(TimeSpan.FromMinutes(15));
        await browser.GetAsync("api/account");

        string second = Decode(GetCookie(cookies).Split('.')[1]).GetProperty("sit").GetString()!;
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("unsigned")]
    public async Task ForgedCookiesAreRejected(string forgery)
    {
        NuDepotTestApplication app = CreateApp();
        CookieContainer cookies = new();
        await SignUpAsync(app, "alice", cookies);
        string[] parts = GetCookie(cookies).Split('.');

        string forged = forgery == "signature"
            ? $"{parts[0]}.{parts[1]}.{Flip(parts[2])}"
            : $"{Encode("""{"alg":"none","typ":"JWT"}""")}.{parts[1]}.";

        using HttpClient browser = app.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "api/account");
        request.Headers.Add("Cookie", $"{CookieName}={forged}");
        using HttpResponseMessage response = await browser.SendAsync(request);

        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("signedIn").GetBoolean());
    }

    [Fact]
    public async Task TheSigningKeyIsKeptForTheNextStart()
    {
        NuDepotTestApplication app = CreateApp();
        CookieContainer cookies = new();
        await SignUpAsync(app, "alice", cookies);

        using (IServiceScope scope = app.Services.CreateScope())
        {
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<NuDepotContext>().SessionSigningKeys.CountAsync());
        }

        // A new instance loads the key from the database, like the feed does when it starts again.
        using SessionSigningKeys keys = new(
            app.Services.GetRequiredService<IServiceScopeFactory>(),
            app.Services.GetRequiredService<IDataProtectionProvider>(),
            TimeProvider.System,
            NullLogger<SessionSigningKeys>.Instance);
        CookieAuthenticationOptions options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(NuDepotAuthenticationSchemes.User);
        SessionTokenFormat format = new(keys, options);

        AuthenticationTicket? ticket = format.Unprotect(GetCookie(cookies));

        Assert.NotNull(ticket);
        Assert.Equal("alice", ticket.Principal.Identity!.Name);
    }

    [Fact]
    public async Task KeysThatCantBeDecryptedAreReplacedAndSignEveryoneOut()
    {
        NuDepotTestApplication app = CreateApp();
        CookieContainer cookies = new();
        await SignUpAsync(app, "alice", cookies);

        // Like when data protection's keys are lost.
        using (IServiceScope scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<NuDepotContext>()
                .SessionSigningKeys
                .ExecuteUpdateAsync(setters => setters.SetProperty(k => k.ProtectedPrivateKey, [1, 2, 3]));
        }

        // The feed starts again.
        using SessionSigningKeys keys = new(
            app.Services.GetRequiredService<IServiceScopeFactory>(),
            app.Services.GetRequiredService<IDataProtectionProvider>(),
            TimeProvider.System,
            NullLogger<SessionSigningKeys>.Instance);
        await keys.LoadAsync(CancellationToken.None);
        CookieAuthenticationOptions options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(NuDepotAuthenticationSchemes.User);

        // It signs with a new key, and the cookies signed with the old one don't work.
        Assert.Null(new SessionTokenFormat(keys, options).Unprotect(GetCookie(cookies)));
        Assert.Single(keys.ValidationKeys);
        using (IServiceScope scope = app.Services.CreateScope())
        {
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<NuDepotContext>().SessionSigningKeys.CountAsync());
        }
    }

    private NuDepotTestApplication CreateApp()
    {
        NuDepotTestApplication app = new(Output, users: true);
        Disposables.Add(app);
        return app;
    }

    private async Task<HttpClient> SignUpAsync(NuDepotTestApplication app, string userName, CookieContainer cookies)
    {
        HttpClient browser = app.CreateBrowserClient(cookies);
        TestAuthenticator authenticator = new();
        Disposables.Add(browser);
        Disposables.Add(authenticator);

        using HttpResponseMessage options = await browser.PostAsync("api/account/signup", JsonContent.Create(new { userName, passkeyName = "Laptop" }));
        string optionsJson = await options.Content.ReadAsStringAsync();
        Assert.True(options.IsSuccessStatusCode, optionsJson);

        using HttpResponseMessage created = await browser.PostAsync(
            $"api/account/passkeys?userName={userName}",
            new StringContent(authenticator.CreateCredential(optionsJson), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);

        return browser;
    }

    private static string GetCookie(CookieContainer cookies) => cookies.GetCookies(new Uri("http://localhost/"))[CookieName]!.Value;

    private static JsonElement Decode(string part)
    {
        string base64 = part.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        return JsonDocument.Parse(Convert.FromBase64String(base64)).RootElement.Clone();
    }

    private static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Changes the signature's first character, keeping it base64url.
    private static string Flip(string signature) => (signature[0] == 'A' ? "B" : "A") + signature[1..];
}
