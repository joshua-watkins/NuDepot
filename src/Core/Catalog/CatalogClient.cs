using NuDepot.Core.Catalog;
using NuDepot.Core.Models;

namespace NuDepot.Core;

public partial class NuGetClientFactory
{
    private class CatalogClient(NuGetClientFactory clientFactory) : ICatalogClient
    {
        private NuGetClientFactory Clientfactory { get; } = clientFactory;

        public async Task<CatalogIndex> GetIndexAsync(CancellationToken cancellationToken = default)
        {
            ICatalogClient client = await Clientfactory.GetCatalogClientAsync(cancellationToken);

            return await client.GetIndexAsync(cancellationToken);
        }

        public async Task<CatalogPage> GetPageAsync(string pageUrl, CancellationToken cancellationToken = default)
        {
            ICatalogClient client = await Clientfactory.GetCatalogClientAsync(cancellationToken);

            return await client.GetPageAsync(pageUrl, cancellationToken);
        }

        public async Task<PackageDetailsCatalogLeaf> GetPackageDetailsLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
        {
            ICatalogClient client = await Clientfactory.GetCatalogClientAsync(cancellationToken);

            return await client.GetPackageDetailsLeafAsync(leafUrl, cancellationToken);
        }

        public async Task<PackageDeleteCatalogLeaf> GetPackageDeleteLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
        {
            ICatalogClient client = await Clientfactory.GetCatalogClientAsync(cancellationToken);

            return await client.GetPackageDeleteLeafAsync(leafUrl, cancellationToken);
        }
    }
}
