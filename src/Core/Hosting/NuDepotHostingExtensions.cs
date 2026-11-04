using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NuDepot.Core.Hosting;

public static class NuDepotHostingExtensions
{
    /// <summary>
    /// Adds the services NuDepot's parts share. Add the parts to the returned builder.
    /// </summary>
    public static NuDepotBuilder AddNuDepot(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<NuDepotFeatures>();
        services.AddRouting(options => options.LowercaseUrls = true);
        services.AddHttpContextAccessor();
        services.AddCors();
        services.AddAuthentication();
        services.AddAuthorization();

        return new NuDepotBuilder(services);
    }

    /// <summary>
    /// Adds the middleware NuDepot's parts share: CORS with the default policy, authentication and
    /// authorization. Use the parts on the returned builder.
    /// </summary>
    public static NuDepotApplicationBuilder UseNuDepot<TApplication>(this TApplication application) where TApplication : IApplicationBuilder, IEndpointRouteBuilder
    {
        ArgumentNullException.ThrowIfNull(application);

        application.UseCors();
        application.UseAuthentication();
        application.UseAuthorization();

        return new NuDepotApplicationBuilder(application, application);
    }
}
