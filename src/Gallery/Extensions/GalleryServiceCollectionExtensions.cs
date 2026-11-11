using Microsoft.Extensions.Options;
using NuDepot.Core.Hosting;
using NuDepot.Core.PackageDetails;
using NuDepot.Core.RepositorySignatures;
using NuDepot.Core.Search;
using NuDepot.Gallery;
using NuDepot.Gallery.Slices;

// In the namespace of IServiceCollection, so applications see AddNuDepotGallery() without a using.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Adds the gallery website to an application's services.
/// </summary>
public static class GalleryServiceCollectionExtensions
{
    /// <summary>
    /// Adds the gallery website. It reads packages from the feed at <see cref="GalleryOptions.ServiceIndexUrl"/>,
    /// or from a feed hosted in the same application. Its <see cref="GalleryOptions"/> are bound from the
    /// <see cref="GalleryOptions.Section"/> configuration section, then changed by <paramref name="configure"/>.
    /// </summary>
    public static NuDepotBuilder AddGallery(this NuDepotBuilder builder, Action<GalleryOptions>? configure = null)
    {
        IServiceCollection services = builder.Services;

        OptionsBuilder<GalleryOptions> options = services
            .AddOptions<GalleryOptions>()
            .BindConfiguration(GalleryOptions.Section);
        if (configure != null)
            options.Configure(configure);

        services.AddSingleton<FeedConnection>();
        services.AddSingleton<ISearchClient>(provider => provider.GetRequiredService<FeedConnection>().ClientFactory.CreateSearchClient());
        services.AddSingleton<IPackageDetailsClient>(provider => provider.GetRequiredService<FeedConnection>().ClientFactory.CreatePackageDetailsClient());
        services.AddSingleton<IRepositorySignaturesClient>(provider => provider.GetRequiredService<FeedConnection>().CreateRepositorySignaturesClient());

        // The models the slices render, which the endpoints load.
        services.AddTransient<IndexModel>();
        services.AddTransient<PackageModel>();
        services.AddTransient<UploadModel>();

        return builder;
    }

    /// <summary>
    /// Adds the gallery website, like <c>services.AddNuDepot().AddGallery(configure)</c>. Its
    /// <see cref="GalleryOptions"/> are bound from the <see cref="GalleryOptions.Section"/> configuration
    /// section, then changed by <paramref name="configure"/>. Use it with
    /// <see cref="Microsoft.AspNetCore.Builder.GalleryApplicationBuilderExtensions.UseNuDepotGallery{TApplication}"/>.
    /// </summary>
    public static NuDepotBuilder AddNuDepotGallery(this IServiceCollection services, Action<GalleryOptions>? configure = null)
    {
        return services
            .AddNuDepot()
            .AddGallery(configure);
    }
}
