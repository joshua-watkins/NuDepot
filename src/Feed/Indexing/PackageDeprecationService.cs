using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Entities;
using NuGet.Versioning;

namespace NuDepot.Feed.Indexing;

/// <inheritdoc />
public class PackageDeprecationService(IPackageDatabase packages, ICatalogService catalog, ILogger<PackageDeprecationService> logger) : IPackageDeprecationService
{
    private IPackageDatabase Packages { get; } = packages;
    private ICatalogService Catalog { get; } = catalog;
    private ILogger<PackageDeprecationService> Logger { get; } = logger;

    /// <inheritdoc />
    public Task<bool> DeprecateAsync(string id, NuGetVersion version, IReadOnlyList<string> reasons, string? message, string? alternatePackageId, string? alternatePackageVersionRange, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Deprecating package {PackageId} {PackageVersion}...", id, version);

        string[] reasonArray = [.. reasons];
        string? range = alternatePackageId == null ? null : alternatePackageVersionRange;
        return UpdateAsync(id, version, setters => setters
            .SetProperty(p => p.DeprecationReasons, reasonArray)
            .SetProperty(p => p.DeprecationMessage, message)
            .SetProperty(p => p.AlternatePackageId, alternatePackageId)
            .SetProperty(p => p.AlternatePackageVersionRange, range),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> UndeprecateAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Undeprecating package {PackageId} {PackageVersion}...", id, version);

        return UpdateAsync(id, version, setters => setters
            .SetProperty(p => p.DeprecationReasons, (string[]?)null)
            .SetProperty(p => p.DeprecationMessage, (string?)null)
            .SetProperty(p => p.AlternatePackageId, (string?)null)
            .SetProperty(p => p.AlternatePackageVersionRange, (string?)null),
            cancellationToken);
    }

    private async Task<bool> UpdateAsync(string id, NuGetVersion version, Action<UpdateSettersBuilder<Package>> setters, CancellationToken cancellationToken)
    {
        if (!await Packages.UpdateAsync(id, version, setters, cancellationToken))
        {
            Logger.LogWarning("Could not find package {PackageId} {PackageVersion}", id, version);
            return false;
        }

        // The catalog records the package as it is now.
        Package package = (await Packages.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken))!;
        await Catalog.AddPackageDetailsAsync([package], cancellationToken);

        return true;
    }
}
