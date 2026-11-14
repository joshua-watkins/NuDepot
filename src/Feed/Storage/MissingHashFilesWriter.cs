using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NuDepot.Feed.Entities;
using NuGet.Versioning;

namespace NuDepot.Feed.Storage;

/// <summary>
/// When the application starts, writes the hash files of packages stored before the feed wrote them, so the packages
/// folder is a NuGet folder feed. It runs in the background. Each start checks that every package has one, which only
/// takes long the first time, when it computes any hashes the database doesn't have.
/// </summary>
/// <param name="logger">The logger.</param>
/// <param name="scopes">Creates the scope the database and storage are used in.</param>
internal sealed partial class MissingHashFilesWriter(ILogger<MissingHashFilesWriter> logger, IServiceScopeFactory scopes) : BackgroundService
{
    private ILogger Logger { get; } = logger;
    private IServiceScopeFactory Scopes { get; } = scopes;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = Scopes.CreateAsyncScope();
            var packages = await scope.ServiceProvider
                .GetRequiredService<NuDepotDbContext>()
                .Packages
                .Select(p => new { p.Id, p.NormalizedVersionString, p.PackageHash })
                .ToListAsync(stoppingToken);

            IPackageStorageService storage = scope.ServiceProvider.GetRequiredService<IPackageStorageService>();
            int written = 0;
            foreach (var package in packages)
            {
                if (await storage.WriteMissingHashAsync(package.Id, NuGetVersion.Parse(package.NormalizedVersionString), package.PackageHash, stoppingToken))
                    written++;
            }

            if (written > 0)
                Log.Written(Logger, written);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            // The feed works without them; only reading the packages folder as a NuGet feed doesn't.
            Log.Failed(Logger, e);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Wrote the hash files of {Count} packages stored before the feed wrote them, so NuGet can read the packages folder as a package source")]
        public static partial void Written(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't write the hash files of packages stored before the feed wrote them. The feed works without them, but NuGet can't read the packages folder as a package source.")]
        public static partial void Failed(ILogger logger, Exception exception);
    }
}
