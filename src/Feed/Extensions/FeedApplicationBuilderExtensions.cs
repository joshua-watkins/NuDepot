using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
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

        // The protocol's registrations are gzipped. They're public metadata, so compressing them over HTTPS gives
        // nothing away. The application's own compression settings are left alone.
        ResponseCompressionProvider registrationCompression = new(app.Services, Options.Create(new ResponseCompressionOptions
        {
            EnableForHttps = true,
            MimeTypes = ["application/json"],
            Providers = { new GzipCompressionProvider(Options.Create(new GzipCompressionProviderOptions())) },
        }));
        app.Application.UseWhen(
            context => IsRegistrationRequest(context.Request.Path),
            registrations => registrations.UseMiddleware<ResponseCompressionMiddleware>(registrationCompression));

        NuDepotEndpointBuilder.MapEndpoints(app.Endpoints);

        // For monitors and load balancers, without a key: whether the feed is Healthy, Degraded (200) or Unhealthy
        // (503), and at "health/live", only whether it answers. Administrators see each check in the gallery.
        app.Endpoints.MapHealthChecks(FeedServiceCollectionExtensions.HealthPath);
        app.Endpoints.MapHealthChecks(FeedServiceCollectionExtensions.HealthPath + "/live", new HealthCheckOptions { Predicate = _ => false });
        GetPipeline(app).UsesFeed = true;

        return app;
    }

    /// <summary>
    /// Maps the endpoints the gallery uses for the feed's users: signing up and signing in with passkeys, and
    /// creating API keys for NuGet clients. It needs users, from <c>AddUsers()</c> or <c>EnableUsers()</c>. If
    /// they're off, because <see cref="UserOptions.IsEnabled"/> is false, it doesn't map anything.
    /// </summary>
    public static NuDepotApplicationBuilder MapUsers(this NuDepotApplicationBuilder app)
    {
        if (app.Services.GetService<FeedFeatures>() is not { Users: true })
            throw new InvalidOperationException("MapUsers() needs the feed's users. Call AddUsers() or EnableUsers() on services.AddNuDepot().");

        if (!app.Services.GetRequiredService<IOptions<UserOptions>>().Value.IsEnabled)
            return app;

        GetPipeline(app).MapsUsers = true;
        NuDepotEndpointBuilder.MapAccountRoutes(app.Endpoints);
        app.Features.Users = true;

        return app;
    }

    private static bool IsRegistrationRequest(PathString path)
        => path.StartsWithSegments("/v3/registration") || path.StartsWithSegments("/v3/registration-semver2");

    private static FeedPipeline GetPipeline(NuDepotApplicationBuilder app)
        => app.Services.GetRequiredService<FeedPipeline>();
}
