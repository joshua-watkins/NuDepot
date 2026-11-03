using System;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Core.Models;
using NuGet.Core.ServiceIndex;

namespace NuGet.Core;

public partial class NuGetClientFactory
{
    private class ServiceIndexClient : IServiceIndexClient
    {
        private readonly NuGetClientFactory _clientFactory;

        public ServiceIndexClient(NuGetClientFactory clientFactory)
        {
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        public async Task<ServiceIndexResponse> GetAsync(CancellationToken cancellationToken = default)
        {
            return await _clientFactory.GetServiceIndexAsync(cancellationToken);
        }
    }
}
