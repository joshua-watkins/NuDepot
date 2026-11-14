using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Storage;
using NuGet.Versioning;

namespace NuDepot.Feed.Indexing;

/// <summary>
/// Deletes packages, by unlisting them or removing them, and records the change in the catalog.
/// </summary>
/// <param name="logger">The logger.</param>
/// <param name="packages">The package database.</param>
/// <param name="storage">Where packages are stored.</param>
/// <param name="catalog">The catalog that records the change.</param>
/// <param name="locks">The packages' locks, so a version isn't deleted while it's pushed.</param>
/// <param name="options">The feed's options.</param>
/// <param name="httpContextAccessor">The request, whose client is logged with what it changes.</param>
public partial class PackageDeletionService(ILogger<PackageDeletionService> logger, IPackageDatabase packages, IPackageStorageService storage, ICatalogService catalog, PackageLocks locks, IOptionsSnapshot<FeedOptions> options, IHttpContextAccessor httpContextAccessor) : IPackageDeletionService
{
    private ILogger Logger { get; } = logger;
    private IPackageDatabase Packages { get; } = packages;
    private IPackageStorageService Storage { get; } = storage;
    private ICatalogService Catalog { get; } = catalog;
    private PackageLocks Locks { get; } = locks;
    private FeedOptions Options { get; } = options.Value;
    private IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;

    // Who's changing the feed, for the logs.
    private string Actor => FeedActor.Describe(HttpContextAccessor.HttpContext);

    /// <inheritdoc />
    public async Task<bool> TryDeletePackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return Options.PackageDeletionBehavior switch
        {
            PackageDeletionBehavior.Unlist => await TryUnlistPackageAsync(id, version, cancellationToken),
            PackageDeletionBehavior.HardDelete => await TryHardDeletePackageAsync(id, version, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown deletion behavior '{Options.PackageDeletionBehavior}'"),
        };
    }

    private async Task<bool> TryUnlistPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        Log.Unlisting(Logger, id, version);

        if (!await Packages.UnlistPackageAsync(id, version, cancellationToken))
        {
            Log.NotFound(Logger, id, version);

            return false;
        }

        Log.Unlisted(Logger, Actor, id, version);

        await AddToCatalogAsync(id, version, cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> TryRelistPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        Log.Relisting(Logger, id, version);

        if (!await Packages.RelistPackageAsync(id, version, cancellationToken))
        {
            Log.NotFound(Logger, id, version);

            return false;
        }

        Log.Relisted(Logger, Actor, id, version);

        await AddToCatalogAsync(id, version, cancellationToken);

        return true;
    }

    private async Task AddToCatalogAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        Package? package = await Packages.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken);
        if (package != null)
            await Catalog.AddPackageDetailsAsync([package], cancellationToken);
    }

    private async Task<bool> TryHardDeletePackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        // A push of the same version stores its files before it records the package, so deleting meanwhile could
        // leave a package without files.
        SemaphoreSlim packageLock = Locks.For(id, version);
        await packageLock.WaitAsync(cancellationToken);
        try
        {
            return await HardDeletePackageAsync(id, version, cancellationToken);
        }
        finally
        {
            packageLock.Release();
        }
    }

    private async Task<bool> HardDeletePackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        Log.HardDeletingFromDatabase(Logger, id, version);

        bool found = await Packages.HardDeletePackageAsync(id, version, cancellationToken);
        if (found)
        {
            Log.HardDeleted(Logger, Actor, id, version);
            await Catalog.AddPackageDeleteAsync(id, version, cancellationToken);
        }
        else
        {
            Log.NotFoundInDatabase(Logger, id, version);
        }

        // Delete the package from storage. This is necessary even if the package isn't
        // in the database to ensure that the storage is consistent with the database.
        Log.HardDeletingFromStorage(Logger, id, version);

        await Storage.DeleteAsync(id, version, cancellationToken);

        Log.HardDeletedFromStorage(Logger, id, version);

        return found;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Unlisting package {PackageId} {PackageVersion}...")]
        public static partial void Unlisting(ILogger logger, string packageId, NuGetVersion packageVersion);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not find package {PackageId} {PackageVersion}")]
        public static partial void NotFound(ILogger logger, string packageId, NuGetVersion packageVersion);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Actor} unlisted package {PackageId} {PackageVersion}")]
        public static partial void Unlisted(ILogger logger, string actor, string packageId, NuGetVersion packageVersion);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Actor} relisted package {PackageId} {PackageVersion}")]
        public static partial void Relisted(ILogger logger, string actor, string packageId, NuGetVersion packageVersion);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Actor} permanently deleted package {PackageId} {PackageVersion}")]
        public static partial void HardDeleted(ILogger logger, string actor, string packageId, NuGetVersion packageVersion);

        [LoggerMessage(Level = LogLevel.Information, Message = "Relisting package {PackageId} {PackageVersion}...")]
        public static partial void Relisting(ILogger logger, string packageId, NuGetVersion packageVersion);

        [LoggerMessage(Level = LogLevel.Information, Message = "Hard deleting package {PackageId} {PackageVersion} from the database...")]
        public static partial void HardDeletingFromDatabase(ILogger logger, string packageId, NuGetVersion packageVersion);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not find package {PackageId} {PackageVersion} in the database")]
        public static partial void NotFoundInDatabase(ILogger logger, string packageId, NuGetVersion packageVersion);

        [LoggerMessage(Level = LogLevel.Information, Message = "Hard deleting package {PackageId} {PackageVersion} from storage...")]
        public static partial void HardDeletingFromStorage(ILogger logger, string packageId, NuGetVersion packageVersion);

        [LoggerMessage(Level = LogLevel.Information, Message = "Hard deleted package {PackageId} {PackageVersion} from storage")]
        public static partial void HardDeletedFromStorage(ILogger logger, string packageId, NuGetVersion packageVersion);
    }
}
