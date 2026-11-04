using NuDepot.Core.Models;

namespace NuDepot.Core.Catalog;

/// <summary>
/// A catalog client for a feed without a catalog. Its catalog is always empty.
/// </summary>
public class NullCatalogClient : ICatalogClient
{
    /// <inheritdoc />
    public Task<CatalogIndex> GetIndexAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CatalogIndex
        {
            CommitTimestamp = DateTimeOffset.MinValue,
            Count = 0,
            Items = []
        });
    }

    /// <inheritdoc />
    public Task<CatalogPage> GetPageAsync(string pageUrl, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"{nameof(NullCatalogClient)} does not support loading catalog pages.");
    }

    /// <inheritdoc />
    public Task<PackageDeleteCatalogLeaf> GetPackageDeleteLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"{nameof(NullCatalogClient)} does not support loading catalog leaves.");
    }

    /// <inheritdoc />
    public Task<PackageDetailsCatalogLeaf> GetPackageDetailsLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"{nameof(NullCatalogClient)} does not support loading catalog leaves.");
    }
}
