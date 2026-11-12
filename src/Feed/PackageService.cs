using Microsoft.Extensions.Logging;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Upstream;
using NuGet.Versioning;

namespace NuDepot.Feed;

/// <summary>
/// Finds packages on the feed, or on its upstream feed when mirroring.
/// </summary>
/// <param name="db">The package database.</param>
/// <param name="upstream">The upstream feed.</param>
/// <param name="indexer">Adds packages from the upstream feed.</param>
/// <param name="logger">The logger.</param>
public class PackageService(IPackageDatabase db, IUpstreamClient upstream, IPackageIndexingService indexer, ILogger<PackageService> logger) : IPackageService
{
    private IPackageDatabase Db { get; } = db;
    private IUpstreamClient Upstream { get; } = upstream;
    private IPackageIndexingService Indexer { get; } = indexer;
    private ILogger<PackageService> Logger { get; } = logger;

    /// <inheritdoc />
    public async Task<IReadOnlyList<NuGetVersion>> FindPackageVersionsAsync(string id, CancellationToken cancellationToken)
    {
        // A package with a pushed version belongs to the feed, so nobody can add versions of it on a mirror.
        IReadOnlyList<Package> localPackages = await Db.FindAsync(id, includeUnlisted: true, cancellationToken);
        IEnumerable<NuGetVersion> localVersions = localPackages.Select(p => p.Version);
        if (localPackages.Any(p => !p.IsMirrored))
            return [.. localVersions];

        // Merge the local package versions into the upstream package versions.
        IReadOnlyList<NuGetVersion> upstreamVersions = await Upstream.ListPackageVersionsAsync(id, cancellationToken);

        if (upstreamVersions.Count == 0)
            return [.. localVersions];
        if (localPackages.Count == 0)
            return upstreamVersions;

        return [.. upstreamVersions.Concat(localVersions).Distinct()];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Package>> FindPackagesAsync(string id, CancellationToken cancellationToken)
    {
        IReadOnlyList<Package> localPackages = await Db.FindAsync(id, includeUnlisted: true, cancellationToken);
        if (localPackages.Any(p => !p.IsMirrored))
            return localPackages;

        IReadOnlyList<Package> upstreamPackages = await Upstream.ListPackagesAsync(id, cancellationToken);

        if (upstreamPackages.Count == 0)
            return localPackages;
        if (localPackages.Count == 0)
            return upstreamPackages;

        // Merge the local packages into the upstream packages.
        Dictionary<NuGetVersion, Package> result = upstreamPackages.ToDictionary(p => p.Version);
        Dictionary<NuGetVersion, Package> local = localPackages.ToDictionary(p => p.Version);

        foreach (KeyValuePair<NuGetVersion, Package> localPackage in local)
        {
            result[localPackage.Key] = localPackage.Value;
        }

        return [.. result.Values];
    }

    /// <inheritdoc />
    public async Task<Package?> FindPackageOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (!await MirrorAsync(id, version, cancellationToken))
            return null;

        return await Db.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return await MirrorAsync(id, version, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddDownloadAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        await Db.AddDownloadAsync(packageId, version, cancellationToken);
    }

    /// <summary>
    /// Index the package from an upstream if it does not exist locally.
    /// </summary>
    /// <param name="id">The package ID to index from an upstream.</param>
    /// <param name="version">The package version to index from an upstream.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>True if the package exists locally or was indexed from an upstream source.</returns>
    private async Task<bool> MirrorAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (await Db.ExistsAsync(id, version, cancellationToken))
            return true;

        // Another version was pushed, so the package belongs to the feed: a mirror's version of it isn't used.
        if (await Db.HasPushedVersionsAsync(id, cancellationToken))
            return false;

        Logger.LogInformation(
            "Package {PackageId} {PackageVersion} does not exist locally. Checking upstream feed...",
            id,
            version);

        try
        {
            using Stream? packageStream = await Upstream.DownloadPackageOrNullAsync(id, version, cancellationToken);
            if (packageStream == null)
            {
                Logger.LogWarning(
                    "Upstream feed does not have package {PackageId} {PackageVersion}",
                    id,
                    version);
                return false;
            }

            Logger.LogInformation(
                "Downloaded package {PackageId} {PackageVersion}, indexing...",
                id,
                version);

            PackageIndexingResult result = await Indexer.MirrorAsync(packageStream, id, version, cancellationToken);

            Logger.LogInformation(
                "Finished indexing package {PackageId} {PackageVersion} from upstream feed with result {Result}",
                id,
                version,
                result);

            // Another download may have mirrored it while this one waited.
            return result is PackageIndexingResult.Success or PackageIndexingResult.PackageAlreadyExists;
        }
        catch (Exception e) when (!(e is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            Logger.LogError(
                e,
                "Failed to index package {PackageId} {PackageVersion} from upstream",
                id,
                version);

            return false;
        }
    }
}
