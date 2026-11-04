using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.Serialization;

namespace NuDepot.Core.ServiceIndex;

/// <summary>
/// The NuGet Service Index client, used to discover other resources.
/// 
/// See https://docs.microsoft.com/en-us/nuget/api/service-index
/// </summary>
/// <param name="httpClient">The HTTP client used to send requests.</param>
/// <param name="serviceIndexUrl">The NuGet server's service index URL.</param>
public class RawServiceIndexClient(HttpClient httpClient, string serviceIndexUrl) : IServiceIndexClient
{
    private HttpClient HttpClient { get; } = httpClient;
    private string ServiceIndexUrl { get; } = serviceIndexUrl;

    /// <inheritdoc />
    public async Task<ServiceIndexResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        return await HttpClient.GetFromJsonAsync(
            ServiceIndexUrl,
            NuGetJsonContext.Default.ServiceIndexResponse,
            cancellationToken);
    }
}
