using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Storage;
using NuGet.Versioning;

namespace NuDepot.Feed.Indexing;

/// <summary>
/// Deletes packages, by unlisting them or removing them, and records the change in the catalog.
/// </summary>
/// <param name="packages">The package database.</param>
/// <param name="storage">Where packages are stored.</param>
/// <param name="catalog">The catalog that records the change.</param>
/// <param name="options">The feed's options.</param>
/// <param name="logger">The logger.</param>
public class PackageDeletionService(IPackageDatabase packages, IPackageStorageService storage, ICatalogService catalog, IOptionsSnapshot<FeedOptions> options, ILogger<PackageDeletionService> logger) : IPackageDeletionService
{
    private IPackageDatabase Packages { get; } = packages;
    private IPackageStorageService Storage { get; } = storage;
    private ICatalogService Catalog { get; } = catalog;
    private FeedOptions Options { get; } = options.Value;
    private ILogger<PackageDeletionService> Logger { get; } = logger;

    /// <inheritdoc />
    public async Task<bool> TryDeletePackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        switch (Options.PackageDeletionBehavior)
        {
            case PackageDeletionBehavior.Unlist:
                return await TryUnlistPackageAsync(id, version, cancellationToken);

            case PackageDeletionBehavior.HardDelete:
                return await TryHardDeletePackageAsync(id, version, cancellationToken);

            default:
                throw new InvalidOperationException($"Unknown deletion behavior '{Options.PackageDeletionBehavior}'");
        }
    }

    private async Task<bool> TryUnlistPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Unlisting package {PackageId} {PackageVersion}...", id, version);

        if (!await Packages.UnlistPackageAsync(id, version, cancellationToken))
        {
            Logger.LogWarning("Could not find package {PackageId} {PackageVersion}", id, version);

            return false;
        }

        Logger.LogInformation("Unlisted package {PackageId} {PackageVersion}", id, version);

        await AddToCatalogAsync(id, version, cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> TryRelistPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Relisting package {PackageId} {PackageVersion}...", id, version);

        if (!await Packages.RelistPackageAsync(id, version, cancellationToken))
        {
            Logger.LogWarning("Could not find package {PackageId} {PackageVersion}", id, version);

            return false;
        }

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
        Logger.LogInformation(
            "Hard deleting package {PackageId} {PackageVersion} from the database...",
            id,
            version);

        bool found = await Packages.HardDeletePackageAsync(id, version, cancellationToken);
        if (found)
        {
            await Catalog.AddPackageDeleteAsync(id, version, cancellationToken);
        }
        else
        {
            Logger.LogWarning(
                "Could not find package {PackageId} {PackageVersion} in the database",
                id,
                version);
        }

        // Delete the package from storage. This is necessary even if the package isn't
        // in the database to ensure that the storage is consistent with the database.
        Logger.LogInformation("Hard deleting package {PackageId} {PackageVersion} from storage...",
            id,
            version);

        await Storage.DeleteAsync(id, version, cancellationToken);

        Logger.LogInformation(
            "Hard deleted package {PackageId} {PackageVersion} from storage",
            id,
            version);

        return found;
    }
}
