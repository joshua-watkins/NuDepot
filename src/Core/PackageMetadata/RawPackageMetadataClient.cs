using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.Serialization;

namespace NuDepot.Core.PackageMetadata;

/// <summary>
/// The client to interact with an upstream source's Package Metadata resource.
/// </summary>
/// <param name="httpClient">The HTTP client used to send requests.</param>
/// <param name="registrationBaseUrl">The NuGet server's registration resource URL.</param>
public class RawPackageMetadataClient(HttpClient httpClient, string registrationBaseUrl) : IPackageMetadataClient
{
    private HttpClient HttpClient { get; } = httpClient;
    private string PackageMetadataUrl { get; } = registrationBaseUrl;

    /// <inheritdoc />
    public async Task<RegistrationIndexResponse?> GetRegistrationIndexOrNullAsync(string packageId, CancellationToken cancellationToken = default)
    {
        string url = $"{PackageMetadataUrl}/{packageId.ToLowerInvariant()}/index.json";

        return await HttpClient.GetFromJsonOrDefaultAsync(url, NuGetJsonContext.Default.RegistrationIndexResponse, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RegistrationPageResponse> GetRegistrationPageAsync(string pageUrl, CancellationToken cancellationToken = default)
    {
        return await HttpClient.GetFromJsonAsync(pageUrl, NuGetJsonContext.Default.RegistrationPageResponse, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RegistrationLeafResponse> GetRegistrationLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
    {
        return await HttpClient.GetFromJsonAsync(leafUrl, NuGetJsonContext.Default.RegistrationLeafResponse, cancellationToken);
    }
}
