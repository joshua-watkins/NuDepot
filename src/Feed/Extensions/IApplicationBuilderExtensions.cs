using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuGet.Feed.Configuration;

namespace NuGet.Feed.Extensions;

public static class IApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the middleware the NuGet feed server needs before routing:
    /// forwarded headers and the configured <see cref="NuGetFeedOptions.PathBase"/>.
    /// </summary>
    public static IApplicationBuilder UseNuGetFeed(this IApplicationBuilder app)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));

        var options = app.ApplicationServices.GetRequiredService<IOptions<NuGetFeedOptions>>().Value;

        app.UseForwardedHeaders();
        app.UsePathBase(options.PathBase);

        return app;
    }

    public static IApplicationBuilder UseOperationCancelledMiddleware(this IApplicationBuilder app)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));

        return app.UseMiddleware<OperationCancelledMiddleware>();
    }

    /// <summary>
    /// Maps the NuGet V3 API endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapNuGetFeed(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));

        new NuGetFeedEndpointBuilder().MapEndpoints(endpoints);

        return endpoints;
    }
}
