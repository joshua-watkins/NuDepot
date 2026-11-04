using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Entities;
using NuGet.Versioning;

namespace NuDepot.Feed.Indexing;

/// <inheritdoc />
public class PackageDeprecationService : IPackageDeprecationService
{
    private readonly IPackageDatabase _packages;
    private readonly NuDepotContext _context;
    private readonly ICatalogService _catalog;
    private readonly ILogger<PackageDeprecationService> _logger;

    public PackageDeprecationService(
        IPackageDatabase packages,
        NuDepotContext context,
        ICatalogService catalog,
        ILogger<PackageDeprecationService> logger)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<bool> DeprecateAsync(
        string id,
        NuGetVersion version,
        IReadOnlyList<string> reasons,
        string? message,
        string? alternatePackageId,
        string? alternatePackageVersionRange,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deprecating package {PackageId} {PackageVersion}...", id, version);

        return UpdateAsync(id, version, package =>
        {
            package.DeprecationReasons = reasons.ToArray();
            package.DeprecationMessage = message;
            package.AlternatePackageId = alternatePackageId;
            package.AlternatePackageVersionRange = alternatePackageId == null ? null : alternatePackageVersionRange;
        },
        cancellationToken);
    }

    public Task<bool> UndeprecateAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Undeprecating package {PackageId} {PackageVersion}...", id, version);

        return UpdateAsync(id, version, package =>
        {
            package.DeprecationReasons = null;
            package.DeprecationMessage = null;
            package.AlternatePackageId = null;
            package.AlternatePackageVersionRange = null;
        },
        cancellationToken);
    }

    private async Task<bool> UpdateAsync(
        string id,
        NuGetVersion version,
        Action<Package> update,
        CancellationToken cancellationToken)
    {
        var package = await _packages.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken);
        if (package == null)
        {
            _logger.LogWarning("Could not find package {PackageId} {PackageVersion}", id, version);
            return false;
        }

        update(package);
        await _context.SaveChangesAsync(cancellationToken);
        await _catalog.AddPackageDetailsAsync(new[] { package }, cancellationToken);

        return true;
    }
}
