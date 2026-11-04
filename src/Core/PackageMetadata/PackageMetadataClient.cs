using NuDepot.Core.Models;
using NuDepot.Core.PackageMetadata;

namespace NuDepot.Core;

public partial class NuGetClientFactory
{
    private class PackageMetadataClient(NuGetClientFactory clientFactory) : IPackageMetadataClient
    {
        private NuGetClientFactory Clientfactory { get; } = clientFactory;

        public async Task<RegistrationIndexResponse?> GetRegistrationIndexOrNullAsync(string packageId, CancellationToken cancellationToken = default)
        {
            IPackageMetadataClient client = await Clientfactory.GetPackageMetadataClientAsync(cancellationToken);

            return await client.GetRegistrationIndexOrNullAsync(packageId, cancellationToken);
        }

        public async Task<RegistrationPageResponse> GetRegistrationPageAsync(string pageUrl, CancellationToken cancellationToken = default)
        {
            IPackageMetadataClient client = await Clientfactory.GetPackageMetadataClientAsync(cancellationToken);

            return await client.GetRegistrationPageAsync(pageUrl, cancellationToken);
        }

        public async Task<RegistrationLeafResponse> GetRegistrationLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
        {
            IPackageMetadataClient client = await Clientfactory.GetPackageMetadataClientAsync(cancellationToken);

            return await client.GetRegistrationLeafAsync(leafUrl, cancellationToken);
        }
    }
}
