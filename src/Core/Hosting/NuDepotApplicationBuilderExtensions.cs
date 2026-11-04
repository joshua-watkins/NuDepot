using System;
using Microsoft.AspNetCore.Routing;
using NuDepot.Core.Hosting;

// In the namespace of IApplicationBuilder, so applications see UseNuDepot() without a using.
namespace Microsoft.AspNetCore.Builder;

public static class NuDepotApplicationBuilderExtensions
{
    // Where the application keeps its NuDepot pipeline, so its middleware is only added once.
    private const string ApplicationBuilderProperty = "NuDepot.ApplicationBuilder";

    /// <summary>
    /// Adds the middleware NuDepot's parts share: CORS with the default policy, authentication and
    /// authorization. Use the parts on the returned builder. The middleware is only added the first
    /// time, so it's safe to call more than once, like <c>UseNuDepotFeed()</c> and <c>UseNuDepotGallery()</c> do.
    /// </summary>
    public static NuDepotApplicationBuilder UseNuDepot<TApplication>(this TApplication application) where TApplication : IApplicationBuilder, IEndpointRouteBuilder
    {
        ArgumentNullException.ThrowIfNull(application);

        if (application.Properties.TryGetValue(ApplicationBuilderProperty, out var existing) &&
            existing is NuDepotApplicationBuilder builder)
        {
            return builder;
        }

        application.UseCors();
        application.UseAuthentication();
        application.UseAuthorization();

        builder = new NuDepotApplicationBuilder(application, application);
        application.Properties[ApplicationBuilderProperty] = builder;

        return builder;
    }
}
