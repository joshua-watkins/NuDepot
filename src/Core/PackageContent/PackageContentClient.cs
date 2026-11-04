using NuDepot.Core.Models;
using NuDepot.Core.PackageContent;
using NuGet.Versioning;

namespace NuDepot.Core;

public partial class NuGetClientFactory
{
    private class PackageContentClient(NuGetClientFactory clientFactory) : IPackageContentClient
    {
        private NuGetClientFactory Clientfactory { get; } = clientFactory;

        public async Task<Stream?> DownloadPackageOrNullAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
        {
            IPackageContentClient client = await Clientfactory.GetPackageContentClientAsync(cancellationToken);

            return await client.DownloadPackageOrNullAsync(packageId, packageVersion, cancellationToken);
        }

        public async Task<Stream?> DownloadPackageManifestOrNullAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
        {
            IPackageContentClient client = await Clientfactory.GetPackageContentClientAsync(cancellationToken);

            return await client.DownloadPackageManifestOrNullAsync(packageId, packageVersion, cancellationToken);
        }

        public async Task<PackageVersionsResponse?> GetPackageVersionsOrNullAsync(string packageId, CancellationToken cancellationToken = default)
        {
            IPackageContentClient client = await Clientfactory.GetPackageContentClientAsync(cancellationToken);

            return await client.GetPackageVersionsOrNullAsync(packageId, cancellationToken);
        }
    }
}
