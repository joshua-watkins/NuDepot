using Microsoft.Extensions.Logging;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Upstream;
using NuGet.Versioning;

namespace NuDepot.Feed;

public class PackageService(IPackageDatabase db, IUpstreamClient upstream, IPackageIndexingService indexer, ILogger<PackageService> logger) : IPackageService
{
    private IPackageDatabase Db { get; } = db;
    private IUpstreamClient Upstream { get; } = upstream;
    private IPackageIndexingService Indexer { get; } = indexer;
    private ILogger<PackageService> Logger { get; } = logger;

    public async Task<IReadOnlyList<NuGetVersion>> FindPackageVersionsAsync(string id, CancellationToken cancellationToken)
    {
        IReadOnlyList<NuGetVersion> upstreamVersions = await Upstream.ListPackageVersionsAsync(id, cancellationToken);

        // Merge the local package versions into the upstream package versions.
        IReadOnlyList<Package> localPackages = await Db.FindAsync(id, includeUnlisted: true, cancellationToken);
        IEnumerable<NuGetVersion> localVersions = localPackages.Select(p => p.Version);

        if (!upstreamVersions.Any())
            return [.. localVersions];
        if (!localPackages.Any())
            return upstreamVersions;

        return [.. upstreamVersions.Concat(localVersions).Distinct()];
    }

    public async Task<IReadOnlyList<Package>> FindPackagesAsync(string id, CancellationToken cancellationToken)
    {
        IReadOnlyList<Package> upstreamPackages = await Upstream.ListPackagesAsync(id, cancellationToken);
        IReadOnlyList<Package> localPackages = await Db.FindAsync(id, includeUnlisted: true, cancellationToken);

        if (!upstreamPackages.Any())
            return localPackages;
        if (!localPackages.Any())
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

    public async Task<Package?> FindPackageOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (!await MirrorAsync(id, version, cancellationToken))
            return null;

        return await Db.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return await MirrorAsync(id, version, cancellationToken);
    }

    public async Task AddDownloadAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        await Db.AddDownloadAsync(packageId, version, cancellationToken);
    }

    /// <summary>
    /// Index the package from an upstream if it does not exist locally.
    /// </summary>
    /// <param name="id">The package ID to index from an upstream.</param>
    /// <param name="version">The package version to index from an upstream.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>True if the package exists locally or was indexed from an upstream source.</returns>
    private async Task<bool> MirrorAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        if (await Db.ExistsAsync(id, version, cancellationToken))
            return true;

        Logger.LogInformation(
            "Package {PackageId} {PackageVersion} does not exist locally. Checking upstream feed...",
            id,
            version);

        try
        {
            using (Stream? packageStream = await Upstream.DownloadPackageOrNullAsync(id, version, cancellationToken))
            {
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

                PackageIndexingResult result = await Indexer.IndexAsync(packageStream, cancellationToken);

                Logger.LogInformation(
                    "Finished indexing package {PackageId} {PackageVersion} from upstream feed with result {Result}",
                    id,
                    version,
                    result);

                return result == PackageIndexingResult.Success;
            }
        }
        catch (Exception e)
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
