using NuDepot.Core.Models;
using NuDepot.Core.Search;

namespace NuDepot.Core;

public partial class NuGetClientFactory
{
    private class AutocompleteClient(NuGetClientFactory clientFactory) : IAutocompleteClient
    {
        private NuGetClientFactory Clientfactory { get; } = clientFactory;

        public async Task<AutocompleteResponse> AutocompleteAsync(string? query = null, int skip = 0, int take = 20, bool includePrerelease = true, bool includeSemVer2 = true, CancellationToken cancellationToken = default)
        {
            // TODO: Support search failover.
            IAutocompleteClient client = await Clientfactory.GetAutocompleteClientAsync(cancellationToken);

            return await client.AutocompleteAsync(query, skip, take, includePrerelease, includeSemVer2, cancellationToken);
        }

        public async Task<AutocompleteResponse> ListPackageVersionsAsync(string packageId, bool includePrerelease = true, bool includeSemVer2 = true, CancellationToken cancellationToken = default)
        {
            // TODO: Support search failover.
            IAutocompleteClient client = await Clientfactory.GetAutocompleteClientAsync(cancellationToken);

            return await client.ListPackageVersionsAsync(packageId, includePrerelease, includeSemVer2, cancellationToken);
        }
    }
}
