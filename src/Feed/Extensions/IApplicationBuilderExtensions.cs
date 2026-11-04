using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;

namespace NuDepot.Feed.Extensions;

public static class IApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the middleware the NuGet feed server needs before routing:
    /// forwarded headers and the configured <see cref="NuDepotOptions.PathBase"/>.
    /// </summary>
    public static IApplicationBuilder UseNuDepot(this IApplicationBuilder app)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));

        var options = app.ApplicationServices.GetRequiredService<IOptions<NuDepotOptions>>().Value;

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
    public static IEndpointRouteBuilder MapNuDepot(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));

        new NuDepotEndpointBuilder().MapEndpoints(endpoints);

        return endpoints;
    }
}
