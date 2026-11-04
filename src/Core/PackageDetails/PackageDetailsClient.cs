using NuDepot.Core.Models;
using NuDepot.Core.PackageDetails;
using NuGet.Versioning;

namespace NuDepot.Core;

public partial class NuGetClientFactory
{
    private class PackageDetailsClient(NuGetClientFactory clientFactory) : IPackageDetailsClient
    {
        private NuGetClientFactory Clientfactory { get; } = clientFactory;

        public async Task<IReadOnlyList<NuDepotPackageMetadata>> GetPackageMetadataAsync(string packageId, CancellationToken cancellationToken = default)
        {
            IPackageDetailsClient client = await Clientfactory.GetPackageDetailsClientAsync(cancellationToken);

            return await client.GetPackageMetadataAsync(packageId, cancellationToken);
        }

        public async Task<Stream?> GetPackageReadmeStreamOrNullAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
        {
            IPackageDetailsClient client = await Clientfactory.GetPackageDetailsClientAsync(cancellationToken);

            return await client.GetPackageReadmeStreamOrNullAsync(packageId, packageVersion, cancellationToken);
        }

        public async Task<DependentsResponse> GetPackageDependentsAsync(string packageId, CancellationToken cancellationToken = default)
        {
            IPackageDetailsClient client = await Clientfactory.GetPackageDetailsClientAsync(cancellationToken);

            return await client.GetPackageDependentsAsync(packageId, cancellationToken);
        }
    }
}
