using NuDepot.Core.Models;
using NuDepot.Core.RepositorySignatures;

namespace NuDepot.Core;

public partial class NuGetClientFactory
{
    private class RepositorySignaturesClient(NuGetClientFactory clientFactory) : IRepositorySignaturesClient
    {
        private NuGetClientFactory ClientFactory { get; } = clientFactory;

        public async Task<RepositorySignaturesResponse?> GetOrNullAsync(CancellationToken cancellationToken = default)
        {
            IRepositorySignaturesClient client = await ClientFactory.GetRepositorySignaturesClientAsync(cancellationToken);

            return await client.GetOrNullAsync(cancellationToken);
        }
    }
}
