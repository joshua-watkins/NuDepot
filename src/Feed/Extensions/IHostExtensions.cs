using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NuGet.Feed.Catalog;
using NuGet.Feed.Configuration;
using NuGet.Feed.Entities;
using NuGet.Feed.Validation;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.Feed.Extensions;

public static class IHostExtensions
{
    /// <summary>
    /// The environment variable that holds the folder to read appsettings.json from,
    /// if it isn't the application's content root.
    /// </summary>
    public const string ConfigRootVariable = "NUGETFEED_CONFIG_ROOT";

    /// <summary>
    /// Reads the appsettings files from the folder in <see cref="ConfigRootVariable"/>, if it is set.
    /// The files keep their precedence relative to environment variables and command line arguments.
    /// </summary>
    public static IHostApplicationBuilder UseNuGetFeedConfigRoot(this IHostApplicationBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        var root = Environment.GetEnvironmentVariable(ConfigRootVariable);
        if (string.IsNullOrEmpty(root)) return builder;

        var fileProvider = new PhysicalFileProvider(Path.GetFullPath(root));
        var sources = builder.Configuration.Sources;

        for (var i = 0; i < sources.Count; i++)
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

    public static async Task RunMigrationsAsync(
        this IHost host,
        CancellationToken cancellationToken = default)
    {
        // Run migrations if necessary.
        var options = host.Services.GetRequiredService<IOptions<NuGetFeedOptions>>();

        if (options.Value.RunMigrationsAtStartup)
        {
            using (var scope = host.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetService<NuGetFeedContext>();
                if (context != null)
                {
                    await context.RunMigrationsAsync(cancellationToken);

                    // Packages indexed before the catalog existed are added to it once.
                    var catalog = scope.ServiceProvider.GetService<ICatalogService>();
                    if (catalog != null)
                    {
                        await catalog.InitializeAsync(cancellationToken);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Validates the startup options for the passed <see cref="IHost"/> instance
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public static bool ValidateStartupOptions(this IHost host)
    {
        return host
            .Services
            .GetRequiredService<ValidateStartupOptions>()
            .Validate();
    }
}
