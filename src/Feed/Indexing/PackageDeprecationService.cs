using Microsoft.Extensions.Logging;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Entities;
using NuGet.Versioning;

namespace NuDepot.Feed.Indexing;

/// <inheritdoc />
public class PackageDeprecationService(IPackageDatabase packages, NuDepotContext context, ICatalogService catalog, ILogger<PackageDeprecationService> logger) : IPackageDeprecationService
{
    private IPackageDatabase Packages { get; } = packages;
    private NuDepotContext Context { get; } = context;
    private ICatalogService Catalog { get; } = catalog;
    private ILogger<PackageDeprecationService> Logger { get; } = logger;

    /// <inheritdoc />
    public Task<bool> DeprecateAsync(string id, NuGetVersion version, IReadOnlyList<string> reasons, string? message, string? alternatePackageId, string? alternatePackageVersionRange, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Deprecating package {PackageId} {PackageVersion}...", id, version);

        return UpdateAsync(id, version, package =>
        {
            package.DeprecationReasons = [.. reasons];
            package.DeprecationMessage = message;
            package.AlternatePackageId = alternatePackageId;
            package.AlternatePackageVersionRange = alternatePackageId == null ? null : alternatePackageVersionRange;
        },
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> UndeprecateAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Undeprecating package {PackageId} {PackageVersion}...", id, version);

        return UpdateAsync(id, version, package =>
        {
            package.DeprecationReasons = null;
            package.DeprecationMessage = null;
            package.AlternatePackageId = null;
            package.AlternatePackageVersionRange = null;
        },
        cancellationToken);
    }

    private async Task<bool> UpdateAsync(string id, NuGetVersion version, Action<Package> update, CancellationToken cancellationToken)
    {
        Package? package = await Packages.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken);
        if (package == null)
        {
            Logger.LogWarning("Could not find package {PackageId} {PackageVersion}", id, version);
            return false;
        }

        update(package);
        await Context.SaveChangesAsync(cancellationToken);
        await Catalog.AddPackageDetailsAsync([package], cancellationToken);

        return true;
    }
}
