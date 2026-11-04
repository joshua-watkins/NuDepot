using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
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
        await using var app = await StartAsync(new Dictionary<string, string?>());
        using var client = app.GetTestClient();

        using var index = await client.GetAsync("v3/index.json");
        using var account = await client.GetAsync("api/account");

        Assert.Equal(HttpStatusCode.OK, index.StatusCode);

        // Users aren't enabled, so the account endpoints aren't there.
        Assert.Equal(HttpStatusCode.NotFound, account.StatusCode);
    }

    [Fact]
    public async Task ShortcutsUseApiKeysAndPasskeysWhenTheOptionsTurnThemOn()
    {
        await using var app = await StartAsync(new Dictionary<string, string?>
        {
            { "Feed:ApiKey", "secret" },
            { "Feed:Authorization:AnonymousRole", "None" },
            { "Feed:Authorization:Users:Enabled", "true" },
        });
        using var client = app.GetTestClient();

        using var anonymous = await client.GetAsync("v3/index.json");
        client.DefaultRequestHeaders.Add("X-NuGet-ApiKey", "secret");
        using var withKey = await client.GetAsync("v3/index.json");
        using var account = await client.GetAsync("api/account");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withKey.StatusCode);
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
    }

    [Fact]
    public async Task ConfigureChangesTheBoundOptions()
    {
        await using var app = await StartAsync(
            new Dictionary<string, string?>(),
            options => options.IsReadOnlyMode = true);

        Assert.True(app.Services.GetRequiredService<IOptions<FeedOptions>>().Value.IsReadOnlyMode);
    }

    [Fact]
    public async Task TheSharedPipelineIsOnlyAddedOnce()
    {
        await using var app = await StartAsync(new Dictionary<string, string?>());

        // UseNuDepotGallery() calls UseNuDepot() too, which returns the same pipeline.
        Assert.Same(app.UseNuDepot(), app.UseNuDepot());
    }

    private static async Task<WebApplication> StartAsync(
        Dictionary<string, string?> configuration,
        Action<FeedOptions>? configure = null)
    {
        var testId = Guid.NewGuid().ToString("N");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
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

        var app = builder.Build();
        app.UseNuDepotFeed();

        await app.RunMigrationsAsync();
        await app.StartAsync();

        return app;
    }
}
