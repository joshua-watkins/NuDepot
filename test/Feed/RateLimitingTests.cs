using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Core;
using NuDepot.Core.Hosting;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Entities;
using NuDepot.Feed.RateLimiting;
using NuDepot.Feed.Tests.Support;
using System.Net;
using System.Security.Claims;
using System.Text;
using Xunit;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Each client may only send so many requests: a user wherever they send from, and anyone else from their address.
/// </summary>
public sealed class RateLimitingTests(ITestOutputHelper output) : IDisposable
{
    private const string ReadWriteKey = "read-write-key-0123456789";
    private const string Client = "203.0.113.7";
    private const string OtherClient = "203.0.113.8";

    private LogRecorder Logs { get; } = new();
    private List<NuDepotTestApplication> Apps { get; } = [];

    public void Dispose()
    {
        foreach (NuDepotTestApplication app in Apps)
            app.Dispose();
    }

    [Fact]
    public async Task AClientThatSendsTooManyRequestsIsRefusedAndOthersArent()
    {
        NuDepotTestApplication app = CreateApp(new()
        {
            ["RateLimiting:RequestBurst"] = "2",
            ["RateLimiting:RequestsPerSecond"] = "1",
        });

        Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, Client));
        Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, Client));

        // One more request waits up to a second for its turn, and those after it are refused. The limit gets
        // one more each second, which a slow test could use.
        HttpContext[] next = await Task.WhenAll(SendAsync(app, Client), SendAsync(app, Client), SendAsync(app, Client));
        Assert.Contains(next, c => c.Response.StatusCode == StatusCodes.Status200OK);
        HttpResponse refused = next.First(c => c.Response.StatusCode == StatusCodes.Status429TooManyRequests).Response;
        Assert.Contains(refused.Headers.RetryAfter.ToString(), (string[])["1", "2"]);

        Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, OtherClient));

        // It's logged once, not with each request.
        Assert.Single(Logs.Messages(LogLevel.Warning), m => m == $"An anonymous client ({Client}) sent more than 2 requests at once, or more than 1 a second, so some are refused with 429 Too Many Requests");
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    // One machine can have a whole IPv6 /64 network.
    [InlineData("2001:db8:1:2:3:4:5:6", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::9", "2001:db8:1:2::/64")]
    [InlineData(null, "unknown")]
    public void ClientsAreGroupedByAddressOrNetwork(string? address, string expected)
    {
        Assert.Equal(expected, FeedRateLimiter.AddressKey(address == null ? null : IPAddress.Parse(address)));
    }

    [Fact]
    public void UsersAreLimitedWhereverTheySendFromAndKeysByAddress()
    {
        DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = IPAddress.Parse(Client);

        context.User = new ClaimsPrincipal(FeedClaims.CreateIdentity(new User { Key = 5, UserName = "alice", Role = FeedRole.ReadWrite }, NuDepotAuthenticationSchemes.ApiKey));
        Assert.Equal("user 5", FeedRateLimiter.ClientKey(context));

        context.User = new ClaimsPrincipal(FeedClaims.CreateIdentity("ReadWrite", FeedRole.ReadWrite, NuDepotAuthenticationSchemes.ApiKey));
        Assert.Equal(Client, FeedRateLimiter.ClientKey(context));
    }

    [Fact]
    public async Task AnAddressThatSendsTooManyKeysThatArentValidCantUseKeysForAWhile()
    {
        NuDepotTestApplication app = CreateApp(new()
        {
            ["Authorization:ApiKeys:ReadWrite"] = ReadWriteKey,
            ["RateLimiting:FailedAuthenticationLimit"] = "2",
        });

        Assert.NotEqual(StatusCodes.Status429TooManyRequests, await StatusAsync(app, Client, apiKey: "guessed-key-1"));
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, await StatusAsync(app, Client, credentials: "ReadWrite:guessed-key-2"));

        // Even the right key isn't checked, so guessing it can't tell.
        HttpContext blocked = await SendAsync(app, Client, apiKey: ReadWriteKey);
        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
        Assert.InRange(int.Parse(blocked.Response.Headers.RetryAfter!, System.Globalization.CultureInfo.InvariantCulture), 1, 15 * 60);

        // Requests without a key, and other addresses, aren't affected.
        Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, Client));
        Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, OtherClient, apiKey: ReadWriteKey));
        Assert.Single(Logs.Messages(LogLevel.Warning), m => m.StartsWith($"{Client} sent 2 API keys or credentials that aren't valid in 15 minutes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAddressMayOnlyTryToSignUpOrSignInAFewTimes()
    {
        NuDepotTestApplication app = CreateApp(new() { ["RateLimiting:AccountRequestLimit"] = "2" }, users: true);

        Assert.Equal(StatusCodes.Status400BadRequest, await SignInOptionsStatusAsync(app, Client));
        Assert.Equal(StatusCodes.Status400BadRequest, await SignInOptionsStatusAsync(app, Client));
        Assert.Equal(StatusCodes.Status429TooManyRequests, await SignInOptionsStatusAsync(app, Client));
        Assert.Equal(StatusCodes.Status400BadRequest, await SignInOptionsStatusAsync(app, OtherClient));

        // Other account requests aren't limited this way.
        for (int i = 0; i < 3; i++)
            Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, Client, path: "/api/account"));
        Assert.Single(Logs.Messages(LogLevel.Warning), m => m.StartsWith($"{Client} tried to sign up, sign in or add a passkey more than 2 times in 10 minutes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnonymousClientsCanBeLimitedTogether()
    {
        NuDepotTestApplication app = CreateApp(new()
        {
            ["Authorization:ApiKeys:ReadWrite"] = ReadWriteKey,
            ["RateLimiting:AnonymousRequestsPerSecond"] = "1",
        });

        // Ten seconds' worth may come at once, from anywhere, and then one a second.
        for (int i = 0; i < 10; i++)
            Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, $"198.51.100.{i}"));
        Assert.True(await IsRefusedWithinAsync(5, i => StatusAsync(app, $"198.51.100.{10 + i}")));

        Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, "198.51.100.20", apiKey: ReadWriteKey));
        Assert.Single(Logs.Messages(LogLevel.Warning), m => m.StartsWith("Anonymous clients together sent more than 1 requests a second", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnonymousClientsArentLimitedTogetherByDefault()
    {
        NuDepotTestApplication app = CreateApp([]);

        for (int i = 0; i < 20; i++)
            Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, $"198.51.100.{i}"));
    }

    [Fact]
    public async Task HealthChecksArentLimited()
    {
        NuDepotTestApplication app = CreateApp(new()
        {
            ["RateLimiting:RequestBurst"] = "1",
            ["RateLimiting:RequestsPerSecond"] = "1",
        });

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, Client, path: "/health"));
            Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, Client, path: "/health/live"));
        }

        Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, Client));
    }

    [Fact]
    public async Task RateLimitingCanBeTurnedOff()
    {
        // Its settings aren't checked while it's off.
        NuDepotTestApplication app = CreateApp(new()
        {
            ["RateLimiting:IsEnabled"] = "false",
            ["RateLimiting:RequestBurst"] = "0",
            ["RateLimiting:FailedAuthenticationLimit"] = "0",
        });

        for (int i = 0; i < 5; i++)
            Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, Client, apiKey: $"guessed-key-{i}"));
    }

    [Theory]
    [InlineData("RequestBurst", "0", "'Feed:RateLimiting:RequestBurst' must be between 1 and")]
    [InlineData("RequestsPerSecond", "-1", "'Feed:RateLimiting:RequestsPerSecond' must be between 1 and")]
    [InlineData("FailedAuthenticationMinutes", "0", "'Feed:RateLimiting:FailedAuthenticationMinutes' must be between 1 and")]
    [InlineData("AnonymousRequestsPerSecond", "-1", "'Feed:RateLimiting:AnonymousRequestsPerSecond' can't be less than 0")]
    public void TheLimitsMustBePositive(string setting, string value, string message)
    {
        NuDepotTestApplication app = CreateApp(new() { [$"RateLimiting:{setting}"] = value });

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => app.Services);
        Assert.Contains(message, exception.Message);
    }

    [Fact]
    public async Task TheGalleryHostedWithTheFeedSaysWhichBrowserItAsksFor()
    {
        // The gallery calls the feed from this machine, which isn't one of the trusted proxies here.
        NuDepotTestApplication app = CreateApp(new()
        {
            ["Authorization:ApiKeys:ReadWrite"] = ReadWriteKey,
            ["RateLimiting:FailedAuthenticationLimit"] = "1",
            ["TrustedProxies:0"] = "198.51.100.1",
        });
        string token = app.Services.GetRequiredService<NuDepotFeatures>().CoHostedToken;

        await StatusAsync(app, "127.0.0.1", apiKey: "guessed-key", headers: new() { [NuDepotFeatures.CoHostedTokenHeader] = token, ["X-Forwarded-For"] = Client });

        // The key that wasn't valid counted against the browser.
        Assert.Equal(StatusCodes.Status429TooManyRequests, await StatusAsync(app, "127.0.0.1", apiKey: ReadWriteKey, headers: new() { [NuDepotFeatures.CoHostedTokenHeader] = token, ["X-Forwarded-For"] = Client }));
        Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, "127.0.0.1", apiKey: ReadWriteKey, headers: new() { [NuDepotFeatures.CoHostedTokenHeader] = token, ["X-Forwarded-For"] = OtherClient }));

        // Without the secret, the address isn't trusted from this machine.
        Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, "127.0.0.1", apiKey: ReadWriteKey, headers: new() { ["X-Forwarded-For"] = Client }));
        Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, "127.0.0.1", apiKey: ReadWriteKey, headers: new() { [NuDepotFeatures.CoHostedTokenHeader] = new string('0', token.Length), ["X-Forwarded-For"] = Client }));
    }

    [Fact]
    public async Task RequestsThatNeedTheMirrorsAreRefusedWhileTheyreAskedTooOften()
    {
        NuDepotTestApplication upstream = CreateApp([]);
        NuDepotTestApplication app = CreateApp(new() { ["Mirroring:MaxLookupsPerSecond"] = "1" }, upstream: upstream.CreateClient());

        // Ten seconds' worth may be asked at once, and then one a second.
        for (int i = 0; i < 10; i++)
            Assert.Equal(StatusCodes.Status404NotFound, await StatusAsync(app, Client, path: $"/v3/package/missing.{i}/index.json"));

        HttpContext? refused = null;
        for (int i = 10; i < 15 && refused == null; i++)
        {
            HttpContext context = await SendAsync(app, OtherClient, path: $"/v3/package/missing.{i}/index.json");
            if (context.Response.StatusCode == StatusCodes.Status429TooManyRequests)
                refused = context;
        }

        Assert.NotNull(refused);
        Assert.Equal("1", refused.Response.Headers.RetryAfter);

        // Requests that don't need the mirrors are still answered.
        Assert.Equal(StatusCodes.Status200OK, await StatusAsync(app, OtherClient));
        Assert.Single(Logs.Messages(LogLevel.Warning), m => m.StartsWith("The mirrors were asked for packages the feed doesn't have more than 1 times a second", StringComparison.Ordinal));
    }

    private NuDepotTestApplication CreateApp(Dictionary<string, string> configuration, bool users = false, HttpClient? upstream = null)
    {
        NuDepotTestApplication app = new(
            output,
            upstreamClient: upstream,
            configuration: configuration,
            users: users,
            configureServices: services => services.AddSingleton<ILoggerProvider>(Logs));
        Apps.Add(app);
        return app;
    }

    // Whether one of a few requests is refused. A limit that gets more requests each second could let the first through.
    private static async Task<bool> IsRefusedWithinAsync(int requests, Func<int, Task<int>> sendAsync)
    {
        for (int i = 0; i < requests; i++)
        {
            if (await sendAsync(i) == StatusCodes.Status429TooManyRequests)
                return true;
        }

        return false;
    }

    private static async Task<int> SignInOptionsStatusAsync(NuDepotTestApplication app, string address)
    {
        HttpContext context = await app.Server.SendAsync(c =>
        {
            c.Request.Method = HttpMethods.Post;
            c.Request.Path = "/api/account/signin/options";
            c.Request.ContentType = "application/json";
            c.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"userName\":\"nobody\"}"));
            c.Connection.RemoteIpAddress = IPAddress.Parse(address);
        }, TestContext.Current.CancellationToken);

        return context.Response.StatusCode;
    }

    private static async Task<int> StatusAsync(NuDepotTestApplication app, string address, string path = "/v3/index.json", string? apiKey = null, string? credentials = null, Dictionary<string, string>? headers = null)
        => (await SendAsync(app, address, path, apiKey, credentials, headers)).Response.StatusCode;

    private static Task<HttpContext> SendAsync(NuDepotTestApplication app, string address, string path = "/v3/index.json", string? apiKey = null, string? credentials = null, Dictionary<string, string>? headers = null)
    {
        return app.Server.SendAsync(c =>
        {
            c.Request.Method = HttpMethods.Get;
            c.Request.Path = path;
            c.Connection.RemoteIpAddress = IPAddress.Parse(address);
            if (apiKey != null)
                c.Request.Headers["X-NuGet-ApiKey"] = apiKey;
            if (credentials != null)
                c.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
            foreach ((string name, string value) in headers ?? [])
                c.Request.Headers[name] = value;
        }, TestContext.Current.CancellationToken);
    }
}
