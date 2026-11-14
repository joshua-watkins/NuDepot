using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Tests.Support;
using System.Net;
using Xunit;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Hosts the feed the way another application would, with <c>AddNuDepotFeed()</c> and <c>UseNuDepotFeed()</c>.
/// </summary>
public class HostingShortcutTests
{
    [Fact]
    public async Task ShortcutsHostTheFeed()
    {
        await using WebApplication app = await StartAsync(new Dictionary<string, string?>());
        using HttpClient client = app.GetTestClient();

        using HttpResponseMessage index = await client.GetAsync("v3/index.json", TestContext.Current.CancellationToken);
        using HttpResponseMessage account = await client.GetAsync("api/account", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, index.StatusCode);

        // Users aren't enabled, so the account endpoints aren't there.
        Assert.Equal(HttpStatusCode.NotFound, account.StatusCode);
    }

    [Fact]
    public async Task ShortcutsUseTheFeedsApiKeys()
    {
        await using WebApplication app = await StartAsync(new Dictionary<string, string?>
        {
            { "Feed:Authorization:ApiKeys:ReadOnly", "secret" },
        });
        using HttpClient client = app.GetTestClient();

        using HttpResponseMessage anonymous = await client.GetAsync("v3/index.json", TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Add("X-NuGet-ApiKey", "secret");
        using HttpResponseMessage withKey = await client.GetAsync("v3/index.json", TestContext.Current.CancellationToken);
        using HttpResponseMessage account = await client.GetAsync("api/account", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withKey.StatusCode);

        // Users need AddUsers() and MapUsers().
        Assert.Equal(HttpStatusCode.NotFound, account.StatusCode);
    }

    [Fact]
    public async Task UserShortcutsAddUsersToTheFeed()
    {
        await using WebApplication app = await StartAsync(new Dictionary<string, string?>(), users: true);
        using HttpClient client = app.GetTestClient();

        using HttpResponseMessage account = await client.GetAsync("api/account", TestContext.Current.CancellationToken);
        using HttpResponseMessage push = await client.PutAsync("api/v2/package", new MultipartFormDataContent
        {
            { new StreamContent(TestPackages.Create("Anonymous.Package", "1.0.0")), "package", "package.nupkg" },
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, account.StatusCode);

        // With users, anonymous requests may only read.
        Assert.Equal(HttpStatusCode.Unauthorized, push.StatusCode);
    }

    [Fact]
    public async Task ConfigureChangesTheBoundOptions()
    {
        await using WebApplication app = await StartAsync(
            new Dictionary<string, string?>(),
            options => options.IsReadOnlyMode = true);

        Assert.True(app.Services.GetRequiredService<IOptions<FeedOptions>>().Value.IsReadOnlyMode);
    }

    [Fact]
    public async Task TheSharedPipelineIsOnlyAddedOnce()
    {
        await using WebApplication app = await StartAsync(new Dictionary<string, string?>());

        // UseNuDepotGallery() calls UseNuDepot() too, which returns the same pipeline.
        Assert.Same(app.UseNuDepot(), app.UseNuDepot());
    }

    [Fact]
    public async Task AnOpenFeedIsWarnedAbout()
    {
        LogRecorder logs = new();
        await using WebApplication app = await StartAsync(new Dictionary<string, string?>(), logs: logs);

        Assert.Contains(logs.Warnings("NuDepot.Feed"), warning => warning.Contains("anyone who can reach it may push", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Feed:Authorization:ApiKeys:ReadWrite", false)]
    [InlineData("Feed:Authorization:ApiKeys:ReadOnly", false)]
    [InlineData(null, true)]
    public async Task AFeedOthersMayNotPushToIsNotWarnedAbout(string? key, bool users)
    {
        LogRecorder logs = new();
        Dictionary<string, string?> configuration = key == null
            ? []
            : new() { [key] = "a8c1d9e2f0b74c3e9a51d6e7f8b2c4a0" };
        await using WebApplication app = await StartAsync(configuration, users: users, logs: logs);

        Assert.Empty(logs.Warnings("NuDepot.Feed"));
    }

    [Fact]
    public async Task ShortApiKeysAreWarnedAbout()
    {
        LogRecorder logs = new();
        await using WebApplication app = await StartAsync(
            new Dictionary<string, string?>
            {
                { "Feed:Authorization:ApiKeys:ReadWrite", "secret" },
                { "Feed:Authorization:ApiKeys:Admin", "a8c1d9e2f0b74c3e9a51d6e7f8b2c4a0" },
            },
            logs: logs);

        Assert.Equal(
            "The ReadWrite API key has only 6 characters, so it could be guessed. Use a random key of at least 16.",
            Assert.Single(logs.Warnings("NuDepot.Feed")));
    }

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> configuration, Action<FeedOptions>? configure = null, bool users = false, LogRecorder? logs = null)
    {
        string testId = Guid
            .NewGuid()
            .ToString("N");
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (logs != null)
            builder.Logging.AddProvider(logs);

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Feed:Database:ConnectionString", $"Data Source=HostingShortcutTests-{testId};Mode=Memory;Cache=Shared" },
            { "Feed:Storage:Temporary", "true" },
        });
        builder.Configuration.AddInMemoryCollection(configuration);

        builder.Services.AddNuDepotFeed(configure);
        if (users)
            builder.Services.AddNuDepotUsers();

        WebApplication app = builder.Build();
        app.UseNuDepotFeed();
        if (users)
            app.MapNuDepotUsers();

        await app.RunMigrationsAsync();
        await app.StartAsync();

        return app;
    }
}
