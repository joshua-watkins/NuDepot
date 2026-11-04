using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NuDepot.Core.Models;
using NuDepot.Core.PackageDetails;
using NuGet.Versioning;

namespace NuDepot.Core;

public partial class NuGetClientFactory
{
    private class PackageDetailsClient : IPackageDetailsClient
    {
        private readonly NuGetClientFactory _clientfactory;

        public PackageDetailsClient(NuGetClientFactory clientFactory)
        {
            _clientfactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        public async Task<IReadOnlyList<NuDepotPackageMetadata>> GetPackageMetadataAsync(
            string packageId,
            CancellationToken cancellationToken = default)
        {
            var client = await _clientfactory.GetPackageDetailsClientAsync(cancellationToken);

            return await client.GetPackageMetadataAsync(packageId, cancellationToken);
        }

        public async Task<Stream?> GetPackageReadmeStreamOrNullAsync(
            string packageId,
            NuGetVersion packageVersion,
            CancellationToken cancellationToken = default)
        {
            var client = await _clientfactory.GetPackageDetailsClientAsync(cancellationToken);

            return await client.GetPackageReadmeStreamOrNullAsync(packageId, packageVersion, cancellationToken);
        }

        public async Task<DependentsResponse> GetPackageDependentsAsync(
            string packageId,
            CancellationToken cancellationToken = default)
        {
            var client = await _clientfactory.GetPackageDetailsClientAsync(cancellationToken);

            return await client.GetPackageDependentsAsync(packageId, cancellationToken);
        }
    }
}
