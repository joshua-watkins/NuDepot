using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.Serialization;
using NuGet.Versioning;
using System.Net;

namespace NuDepot.Core.PackageDetails;

/// <summary>
/// The client to interact with a NuGet server's package details resources.
/// </summary>
/// <param name="httpClient">The HTTP client used to send requests.</param>
/// <param name="registrationBaseUrl">The NuGet server's registration resource URL.</param>
/// <param name="readmeUriTemplate">The NuGet server's README URI template, or null if the server has none.</param>
/// <param name="dependentsUrl">The NuGet server's package dependents URL, or null if the server has none.</param>
public class RawPackageDetailsClient(HttpClient httpClient, string registrationBaseUrl, string? readmeUriTemplate = null, string? dependentsUrl = null) : IPackageDetailsClient
{
    private HttpClient HttpClient { get; } = httpClient;
    private string PackageMetadataUrl { get; } = registrationBaseUrl;
    private string? ReadmeUriTemplate { get; } = readmeUriTemplate;
    private string? DependentsUrl { get; } = dependentsUrl;

    /// <inheritdoc />
    public async Task<IReadOnlyList<NuDepotPackageMetadata>> GetPackageMetadataAsync(string packageId, CancellationToken cancellationToken = default)
    {
        List<NuDepotPackageMetadata> result = [];

        string url = $"{PackageMetadataUrl}/{packageId.ToLowerInvariant()}/index.json";
        NuDepotRegistrationIndexResponse? registrationIndex = await HttpClient.GetFromJsonOrDefaultAsync(
            url,
            NuGetJsonContext.Default.NuDepotRegistrationIndexResponse,
            cancellationToken);

        if (registrationIndex?.Pages == null)
            return result;

        foreach (NuDepotRegistrationIndexPage page in registrationIndex.Pages)
        {
            // If the package's registration index is too big, it will be split into registration
            // pages stored at different URLs. We will need to fetch each page's items individually.
            // We can detect this case as the registration index will have "null" items.
            IReadOnlyList<NuDepotRegistrationIndexPageItem>? items = page.ItemsOrNull;
            if (items == null)
            {
                NuDepotRegistrationIndexPage externalPage = await HttpClient.GetFromJsonAsync(
                    page.RegistrationPageUrl,
                    NuGetJsonContext.Default.NuDepotRegistrationIndexPage,
                    cancellationToken);

                // Skip malformed external pages.
                if (externalPage.ItemsOrNull == null)
                    continue;

                items = externalPage.ItemsOrNull;
            }

            foreach (NuDepotRegistrationIndexPageItem item in items)
            {
                if (item.PackageMetadata != null)
                    result.Add(item.PackageMetadata);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<Stream?> GetPackageReadmeStreamOrNullAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default)
    {
        if (ReadmeUriTemplate == null)
            return null;

        string url = ReadmeUriTemplate
            .Replace("{lower_id}", packageId.ToLowerInvariant())
            .Replace("{lower_version}", packageVersion.ToNormalizedString().ToLowerInvariant());

        // The response will be disposed when the returned content stream is disposed.
        HttpResponseMessage response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DependentsResponse> GetPackageDependentsAsync(string packageId, CancellationToken cancellationToken = default)
    {
        DependentsResponse? response = null;

        if (DependentsUrl != null)
        {
            string url = $"{DependentsUrl}?packageId={Uri.EscapeDataString(packageId)}";

            response = await HttpClient.GetFromJsonOrDefaultAsync(url, NuGetJsonContext.Default.DependentsResponse, cancellationToken);
        }

        return new DependentsResponse
        {
            TotalHits = response?.TotalHits ?? 0,
            Data = response?.Data ?? [],
        };
    }
}
