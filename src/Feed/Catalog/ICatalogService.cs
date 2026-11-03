using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Core.Models;
using NuGet.Feed.Entities;
using NuGet.Versioning;

namespace NuGet.Feed.Catalog;

/// <summary>
/// Records every change to the feed's packages and serves the record as the catalog resource.
/// See: https://learn.microsoft.com/en-us/nuget/api/catalog-resource
/// </summary>
public interface ICatalogService
{
    /// <summary>
    /// Record the current state of packages that were pushed, unlisted, relisted,
    /// deprecated or had their vulnerabilities changed, in a single commit.
    /// The packages' dependencies and package types must be loaded.
    /// </summary>
    Task AddPackageDetailsAsync(IReadOnlyList<Package> packages, CancellationToken cancellationToken);

    /// <summary>
    /// Record that a package was deleted.
    /// </summary>
    Task AddPackageDeleteAsync(string id, NuGetVersion version, CancellationToken cancellationToken);

    Task<CatalogIndex> GetIndexAsync(CancellationToken cancellationToken);

    /// <returns>The page, or null if it doesn't exist.</returns>
    Task<CatalogPage?> GetPageOrNullAsync(int page, CancellationToken cancellationToken);

    /// <returns>A <see cref="PackageDetailsCatalogLeaf"/> or <see cref="PackageDeleteCatalogLeaf"/>, or null if it doesn't exist.</returns>
    Task<CatalogLeaf?> GetLeafOrNullAsync(int key, CancellationToken cancellationToken);

    /// <summary>
    /// Find the URL of the latest catalog leaf for each version of a package.
    /// </summary>
    /// <returns>Leaf URLs keyed by the lowercase full normalized version.</returns>
    Task<IReadOnlyDictionary<string, string>> FindLatestLeafUrlsAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Add packages that predate the catalog to it, computing their hashes if needed.
    /// Does nothing if the catalog already has entries.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);
}
