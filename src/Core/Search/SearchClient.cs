using NuDepot.Core.Models;
using NuDepot.Core.Search;

namespace NuDepot.Core;

public partial class NuGetClientFactory
{
    private class SearchClient(NuGetClientFactory clientFactory) : ISearchClient
    {
        private NuGetClientFactory Clientfactory { get; } = clientFactory;

        public async Task<SearchResponse> SearchAsync(string? query = null, int skip = 0, int take = 20, bool includePrerelease = true, bool includeSemVer2 = true, string? packageType = null, string? framework = null, CancellationToken cancellationToken = default)
        {
            // TODO: Support search failover.
            ISearchClient client = await Clientfactory.GetSearchClientAsync(cancellationToken);

            return await client.SearchAsync(query, skip, take, includePrerelease, includeSemVer2, packageType, framework, cancellationToken);
        }
    }
}
