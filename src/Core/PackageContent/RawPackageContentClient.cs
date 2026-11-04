using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.Serialization;
using NuGet.Versioning;
using System.Net;

namespace NuDepot.Core.PackageContent;

/// <summary>
/// The client to interact with an upstream source's Package Content resource.
/// </summary>
/// <param name="httpClient">The HTTP client used to send requests.</param>
/// <param name="packageContentUrl">The NuGet Server's package content URL.</param>
public class RawPackageContentClient(HttpClient httpClient, string packageContentUrl) : IPackageContentClient
{
    private HttpClient HttpClient { get; } = httpClient;
    private string PackageContentUrl { get; } = packageContentUrl.TrimEnd('/');

    /// <inheritdoc />
    public async Task<PackageVersionsResponse?> GetPackageVersionsOrNullAsync(string packageId, CancellationToken cancellationToken = default)
    {
        string id = packageId.ToLowerInvariant();
        string url = $"{PackageContentUrl}/{id}/index.json";

        return await HttpClient.GetFromJsonOrDefaultAsync(url, NuGetJsonContext.Default.PackageVersionsResponse, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream?> DownloadPackageOrNullAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
    {
        string id = packageId.ToLowerInvariant();
        string version = packageVersion
            .ToNormalizedString()
            .ToLowerInvariant();

        // The response will be disposed when the returned content stream is disposed.
        string url = $"{PackageContentUrl}/{id}/{version}/{id}.{version}.nupkg";
        HttpResponseMessage response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream?> DownloadPackageManifestOrNullAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
    {
        string id = packageId.ToLowerInvariant();
        string version = packageVersion
            .ToNormalizedString()
            .ToLowerInvariant();

        // The response will be disposed when the returned content stream is disposed.
        string url = $"{PackageContentUrl}/{id}/{version}/{id}.nuspec";
        HttpResponseMessage response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }
}
