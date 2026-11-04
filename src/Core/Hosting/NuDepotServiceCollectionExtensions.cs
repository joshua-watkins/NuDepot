using Microsoft.Extensions.DependencyInjection.Extensions;
using NuDepot.Core.Hosting;

// In the namespace of IServiceCollection, so applications see AddNuDepot() without a using.
namespace Microsoft.Extensions.DependencyInjection;

public static class NuDepotServiceCollectionExtensions
{
    /// <summary>
    /// Adds the services NuDepot's parts share. Add the parts to the returned builder. It's safe to
    /// call more than once, like <c>AddNuDepotFeed()</c> and <c>AddNuDepotGallery()</c> do.
    /// </summary>
    public static NuDepotBuilder AddNuDepot(this IServiceCollection services)
    {
        services.TryAddSingleton<NuDepotFeatures>();
        services.AddRouting(options => options.LowercaseUrls = true);
        services.AddHttpContextAccessor();
        services.AddCors();
        services.AddAuthentication();
        services.AddAuthorization();

        return new NuDepotBuilder(services);
    }
}
