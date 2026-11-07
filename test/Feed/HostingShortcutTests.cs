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

        using HttpResponseMessage index = await client.GetAsync("v3/index.json");
        using HttpResponseMessage account = await client.GetAsync("api/account");

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

        using HttpResponseMessage anonymous = await client.GetAsync("v3/index.json");
        client.DefaultRequestHeaders.Add("X-NuGet-ApiKey", "secret");
        using HttpResponseMessage withKey = await client.GetAsync("v3/index.json");
        using HttpResponseMessage account = await client.GetAsync("api/account");

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

        using HttpResponseMessage account = await client.GetAsync("api/account");
        using HttpResponseMessage push = await client.PutAsync("api/v2/package", new MultipartFormDataContent
        {
            { new StreamContent(TestPackages.Create("Anonymous.Package", "1.0.0")), "package", "package.nupkg" },
        });

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

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> configuration, Action<FeedOptions>? configure = null, bool users = false)
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
