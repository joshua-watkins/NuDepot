using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;

// In the namespace of IHost, so applications see RunMigrationsAsync() without a using.
namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extensions to the host of an application with the NuGet feed.
/// </summary>
public static class FeedHostExtensions
{
    /// <summary>
    /// Updates the package database's schema, and adds packages that predate the catalog to it,
    /// if <see cref="NuDepot.Feed.Configuration.FeedOptions.RunMigrationsAtStartup"/> is true. Call it before running the host.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>A task that completes when the database is up to date.</returns>
    public static async Task RunMigrationsAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        // Run migrations if necessary.
        IOptions<FeedOptions> options = host.Services.GetRequiredService<IOptions<FeedOptions>>();

        if (options.Value.RunMigrationsAtStartup)
        {
            using IServiceScope scope = host.Services.CreateScope();
            NuDepotContext? context = scope.ServiceProvider.GetService<NuDepotContext>();
            if (context != null)
            {
                await context.RunMigrationsAsync(cancellationToken);

                // Packages indexed before the catalog existed are added to it once.
                ICatalogService? catalog = scope.ServiceProvider.GetService<ICatalogService>();
                if (catalog != null)
                    await catalog.InitializeAsync(cancellationToken);
            }
        }
    }
}
