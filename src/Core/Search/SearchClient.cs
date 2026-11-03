using System;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Core.Models;
using NuGet.Core.Search;

namespace NuGet.Core;

public partial class NuGetClientFactory
{
    private class SearchClient : ISearchClient
    {
        private readonly NuGetClientFactory _clientfactory;

        public SearchClient(NuGetClientFactory clientFactory)
        {
            _clientfactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        public async Task<SearchResponse> SearchAsync(
            string? query = null,
            int skip = 0,
            int take = 20,
            bool includePrerelease = true,
            bool includeSemVer2 = true,
            string? packageType = null,
            string? framework = null,
            CancellationToken cancellationToken = default)
        {
            // TODO: Support search failover.
            var client = await _clientfactory.GetSearchClientAsync(cancellationToken);

            return await client.SearchAsync(query, skip, take, includePrerelease, includeSemVer2, packageType, framework, cancellationToken);
        }
    }
}
