using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NuDepot.Core.Hosting;
using NuDepot.Feed;
using NuDepot.Feed.Authentication;
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
    /// who sign in with passkeys, use the builders instead: <c>app.UseNuDepot().UseFeed().UsePasskeys()</c>.
    /// </summary>
    public static NuDepotApplicationBuilder UseNuDepotFeed<TApplication>(this TApplication application)
        where TApplication : IApplicationBuilder, IEndpointRouteBuilder
    {
        return application
            .UseNuDepot()
            .UseFeed();
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
    /// Lets people sign up and sign in with passkeys, and create API keys for NuGet clients, which the feed accepts
    /// as the password of a package source's credentials, with the user's name. Maps the account endpoints the
    /// gallery uses. Each user has a role, like the feed's API keys, and requests without a key or a signed-in user
    /// may only read the feed. Needs <c>AddPasskeys()</c>.
    /// </summary>
    public static NuDepotApplicationBuilder UsePasskeys(this NuDepotApplicationBuilder app)
    {
        FeedPipeline pipeline = GetPipeline(app);
        if (!pipeline.AddsPasskeys)
            throw new InvalidOperationException("UsePasskeys() needs the feed's users. Call AddPasskeys() on services.AddNuDepot().");

        pipeline.UsesPasskeys = true;
        NuDepotEndpointBuilder.MapAccountRoutes(app.Endpoints);
        app.Features.Passkeys = true;

        return app;
    }

    private static FeedPipeline GetPipeline(NuDepotApplicationBuilder app)
        => app.Services.GetRequiredService<FeedPipeline>();
}
