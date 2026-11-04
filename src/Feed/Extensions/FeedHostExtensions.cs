using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;

// In the namespace of IHost, so applications see RunMigrationsAsync() without a using.
namespace Microsoft.Extensions.Hosting;

public static class FeedHostExtensions
{
    /// <summary>
    /// The environment variable that holds the folder to read appsettings.json from,
    /// if it isn't the application's content root.
    /// </summary>
    public const string ConfigRootVariable = "NUDEPOT_CONFIG_ROOT";

    /// <summary>
    /// Reads the appsettings files from the folder in <see cref="ConfigRootVariable"/>, if it is set.
    /// The files keep their precedence relative to environment variables and command line arguments.
    /// </summary>
    public static IHostApplicationBuilder UseNuDepotConfigRoot(this IHostApplicationBuilder builder)
    {
        string? root = Environment.GetEnvironmentVariable(ConfigRootVariable);
        if (string.IsNullOrEmpty(root))
            return builder;

        PhysicalFileProvider fileProvider = new(Path.GetFullPath(root));
        IList<IConfigurationSource> sources = builder.Configuration.Sources;

        for (int i = 0; i < sources.Count; i++)
        {
            if (sources[i] is JsonConfigurationSource json &&
                json.Path != null &&
                json.Path.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
            {
                // Replace the source, rather than modify it, so the configuration reloads.
                sources[i] = new JsonConfigurationSource
                {
                    Path = json.Path,
                    Optional = json.Optional,
                    ReloadOnChange = json.ReloadOnChange,
                    FileProvider = fileProvider,
                };
            }
        }

        return builder;
    }

    public static async Task RunMigrationsAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        // Run migrations if necessary.
        IOptions<FeedOptions> options = host.Services.GetRequiredService<IOptions<FeedOptions>>();

        if (options.Value.RunMigrationsAtStartup)
        {
            using (IServiceScope scope = host.Services.CreateScope())
            {
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
}
