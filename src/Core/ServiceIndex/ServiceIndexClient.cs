using NuDepot.Core.Models;
using NuDepot.Core.ServiceIndex;

namespace NuDepot.Core;

public partial class NuGetClientFactory
{
    private class ServiceIndexClient(NuGetClientFactory clientFactory) : IServiceIndexClient
    {
        private NuGetClientFactory ClientFactory { get; } = clientFactory;

        public async Task<ServiceIndexResponse> GetAsync(CancellationToken cancellationToken = default)
        {
            return await ClientFactory.GetServiceIndexAsync(cancellationToken);
        }
    }
}
