using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Core.Extensions;
using NuGet.Core.Models;
using NuGet.Core.Serialization;
using NuGet.Versioning;

namespace NuGet.Core.PackageDetails;

/// <summary>
/// The client to interact with a NuGet server's package details resources.
/// </summary>
public class RawPackageDetailsClient : IPackageDetailsClient
{
    private readonly HttpClient _httpClient;
    private readonly string _packageMetadataUrl;
    private readonly string? _readmeUriTemplate;
    private readonly string? _dependentsUrl;

    /// <summary>
    /// Create a new Package Details client.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to send requests.</param>
    /// <param name="registrationBaseUrl">The NuGet server's registration resource URL.</param>
    /// <param name="readmeUriTemplate">The NuGet server's README URI template, or null if the server has none.</param>
    /// <param name="dependentsUrl">The NuGet server's package dependents URL, or null if the server has none.</param>
    public RawPackageDetailsClient(
        HttpClient httpClient,
        string registrationBaseUrl,
        string? readmeUriTemplate = null,
        string? dependentsUrl = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _packageMetadataUrl = registrationBaseUrl ?? throw new ArgumentNullException(nameof(registrationBaseUrl));
        _readmeUriTemplate = readmeUriTemplate;
        _dependentsUrl = dependentsUrl;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NuGetFeedPackageMetadata>> GetPackageMetadataAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        var result = new List<NuGetFeedPackageMetadata>();

        var url = $"{_packageMetadataUrl}/{packageId.ToLowerInvariant()}/index.json";
        var registrationIndex = await _httpClient.GetFromJsonOrDefaultAsync(
            url,
            NuGetJsonContext.Default.NuGetFeedRegistrationIndexResponse,
            cancellationToken);

        if (registrationIndex?.Pages == null)
        {
            return result;
        }

        foreach (var page in registrationIndex.Pages)
        {
            // If the package's registration index is too big, it will be split into registration
            // pages stored at different URLs. We will need to fetch each page's items individually.
            // We can detect this case as the registration index will have "null" items.
            var items = page.ItemsOrNull;
            if (items == null)
            {
                var externalPage = await _httpClient.GetFromJsonAsync(
                    page.RegistrationPageUrl,
                    NuGetJsonContext.Default.NuGetFeedRegistrationIndexPage,
                    cancellationToken);

                // Skip malformed external pages.
                if (externalPage.ItemsOrNull == null) continue;

                items = externalPage.ItemsOrNull;
            }

            foreach (var item in items)
            {
                if (item.PackageMetadata != null)
                {
                    result.Add(item.PackageMetadata);
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<Stream?> GetPackageReadmeStreamOrNullAsync(
        string packageId,
        NuGetVersion packageVersion,
        CancellationToken cancellationToken = default)
    {
        if (_readmeUriTemplate == null)
        {
            return null;
        }

        var url = _readmeUriTemplate
            .Replace("{lower_id}", packageId.ToLowerInvariant())
            .Replace("{lower_version}", packageVersion.ToNormalizedString().ToLowerInvariant());

        // The response will be disposed when the returned content stream is disposed.
        var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DependentsResponse> GetPackageDependentsAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        DependentsResponse? response = null;

        if (_dependentsUrl != null)
        {
            var url = $"{_dependentsUrl}?packageId={Uri.EscapeDataString(packageId)}";

            response = await _httpClient.GetFromJsonOrDefaultAsync(url, NuGetJsonContext.Default.DependentsResponse, cancellationToken);
        }

        return new DependentsResponse
        {
            TotalHits = response?.TotalHits ?? 0,
            Data = response?.Data ?? Array.Empty<PackageDependent>(),
        };
    }
}
