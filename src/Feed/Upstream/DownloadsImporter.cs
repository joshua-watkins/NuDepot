using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NuDepot.Feed.Entities;

namespace NuDepot.Feed.Upstream;

/// <summary>
/// Imports packages' download counts from nuget.org, for the "import downloads" command.
/// </summary>
/// <param name="logger">The logger.</param>
/// <param name="context">The package database.</param>
/// <param name="downloadsSource">Where the download counts come from.</param>
internal partial class DownloadsImporter(ILogger<DownloadsImporter> logger, NuDepotDbContext context, IPackageDownloadsSource downloadsSource)
{
    private const int BatchSize = 200;

    private ILogger Logger { get; } = logger;
    private NuDepotDbContext Context { get; } = context;
    private IPackageDownloadsSource DownloadsSource { get; } = downloadsSource;

    /// <summary>
    /// Sets the download counts of the feed's packages.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>A task that completes when every package is updated.</returns>
    public async Task ImportAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, Dictionary<string, long>> packageDownloads = await DownloadsSource.GetPackageDownloadsAsync();
        int packages = await Context.Packages.CountAsync(cancellationToken);
        int batches = packages / BatchSize + 1;

        for (int batch = 0; batch < batches; batch++)
        {
            Log.Importing(Logger, batch);

            var packageBatch = await Context.Packages
                .OrderBy(p => p.Key)
                .Skip(batch * BatchSize)
                .Take(BatchSize)
                .Select(p => new { p.Key, p.Id, p.NormalizedVersionString })
                .ToListAsync(cancellationToken);

            foreach (var package in packageBatch)
            {
                if (!packageDownloads.TryGetValue(package.Id.ToLowerInvariant(), out Dictionary<string, long>? versions) ||
                    !versions.TryGetValue(package.NormalizedVersionString.ToLowerInvariant(), out long downloads))
                    continue;

                await Context.Packages
                    .Where(p => p.Key == package.Key)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Downloads, downloads), cancellationToken);
            }

            Log.Imported(Logger, batch);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Importing batch {Batch}...")]
        public static partial void Importing(ILogger logger, int batch);

        [LoggerMessage(Level = LogLevel.Information, Message = "Imported batch {Batch}")]
        public static partial void Imported(ILogger logger, int batch);
    }
}
