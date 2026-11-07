using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuDepot.Core.Hosting;
using NuDepot.Feed;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Extensions;

// In the namespace of IApplicationBuilder, so applications see UseNuDepotFeed() without a using.
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Uses the NuGet feed in an application's request pipeline.
/// </summary>
public static class FeedApplicationBuilderExtensions
{
    /// <summary>
    /// Uses the NuGet feed, like <c>app.UseNuDepot().UseFeed()</c>, with its API keys, one for each role. For users
    /// who sign in with passkeys, add <see cref="MapNuDepotUsers{TApplication}"/>.
    /// </summary>
    public static NuDepotApplicationBuilder UseNuDepotFeed<TApplication>(this TApplication application)
        where TApplication : IApplicationBuilder, IEndpointRouteBuilder
    {
        return application
            .UseNuDepot()
            .UseFeed();
    }

    /// <summary>
    /// Maps the endpoints of the feed's users, like <c>app.UseNuDepot().MapUsers()</c>, in an application that uses
    /// the feed, like with <see cref="UseNuDepotFeed{TApplication}"/>. Needs <c>AddNuDepotUsers()</c>.
    /// </summary>
    public static NuDepotApplicationBuilder MapNuDepotUsers<TApplication>(this TApplication application)
        where TApplication : IApplicationBuilder, IEndpointRouteBuilder
    {
        return application
            .UseNuDepot()
            .MapUsers();
    }

    /// <summary>
    /// Maps the NuGet V3 API endpoints. Each requires the permission for what it does. The feed's API keys, one
    /// for each role, decide what anonymous requests may do: without keys, anyone may read, push and unlist.
    /// </summary>
    public static NuDepotApplicationBuilder UseFeed(this NuDepotApplicationBuilder app)
    {
        app.Application.UseMiddleware<OperationCancelledMiddleware>();
        new NuDepotEndpointBuilder().MapEndpoints(app.Endpoints);
        GetPipeline(app).UsesFeed = true;

        return app;
    }

    /// <summary>
    /// Maps the endpoints the gallery uses for the feed's users: signing up and signing in with passkeys, and
    /// creating API keys for NuGet clients. What users may do comes from <c>AddUsers()</c>, which it needs.
    /// </summary>
    public static NuDepotApplicationBuilder MapUsers(this NuDepotApplicationBuilder app)
    {
        if (!app.Services.GetRequiredService<IOptions<FeedOptions>>().Value.Authorization.Users.IsEnabled)
            throw new InvalidOperationException("MapUsers() needs the feed's users. Call AddUsers() on services.AddNuDepot().");

        GetPipeline(app).MapsUsers = true;
        NuDepotEndpointBuilder.MapAccountRoutes(app.Endpoints);
        app.Features.Users = true;

        return app;
    }

    private static FeedPipeline GetPipeline(NuDepotApplicationBuilder app)
        => app.Services.GetRequiredService<FeedPipeline>();
}
