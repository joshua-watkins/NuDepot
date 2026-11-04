using System;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NuDepot.Core.Hosting;
using NuDepot.Feed;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Extensions;

// In the namespace of IApplicationBuilder, so applications see UseNuDepotFeed() without a using.
namespace Microsoft.AspNetCore.Builder;

public static class FeedApplicationBuilderExtensions
{
    /// <summary>
    /// Uses the NuGet feed with everything it offers, like
    /// <c>app.UseNuDepot().UseFeed().UseApiKeys().UsePasskeys()</c>. The feed's options decide what's on:
    /// it's open until it has API keys or users.
    /// </summary>
    public static NuDepotApplicationBuilder UseNuDepotFeed<TApplication>(this TApplication application)
        where TApplication : IApplicationBuilder, IEndpointRouteBuilder
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.UseNuDepot().UseFeed().UseApiKeys().UsePasskeys();
    }

    /// <summary>
    /// Maps the NuGet V3 API endpoints. Each requires the permission for what it does, which
    /// anyone has while the feed is open.
    /// </summary>
    public static NuDepotApplicationBuilder UseFeed(this NuDepotApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Application.UseMiddleware<OperationCancelledMiddleware>();
        new NuDepotEndpointBuilder().MapEndpoints(app.Endpoints);
        GetPipeline(app).UsesFeed = true;

        return app;
    }

    /// <summary>
    /// Accepts API keys: the configured keys, and the keys users create. Without it, requests are anonymous.
    /// </summary>
    public static NuDepotApplicationBuilder UseApiKeys(this NuDepotApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        GetPipeline(app).UsesApiKeys = true;

        return app;
    }

    /// <summary>
    /// Lets people sign in with passkeys and create their own API keys, if
    /// <see cref="UserOptions.Enabled"/> is true. Maps the account endpoints the gallery uses.
    /// </summary>
    public static NuDepotApplicationBuilder UsePasskeys(this NuDepotApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        GetPipeline(app).UsesPasskeys = true;

        if (app.Services.GetRequiredService<FeedAccess>().UsersEnabled)
        {
            NuDepotEndpointBuilder.MapAccountRoutes(app.Endpoints);
            app.Features.Passkeys = true;
        }

        return app;
    }

    private static FeedPipeline GetPipeline(NuDepotApplicationBuilder app)
        => app.Services.GetRequiredService<FeedPipeline>();
}
