using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace NuDepot.Core.Catalog;

/// <summary>
/// The client to interact with a feed's catalog resource.
/// </summary>
/// <param name="httpClient">The HTTP client used to send requests.</param>
/// <param name="catalogUrl">The URL of the feed's catalog index.</param>
public class RawCatalogClient(HttpClient httpClient, string catalogUrl) : ICatalogClient
{
    private HttpClient HttpClient { get; } = httpClient;
    private string CatalogUrl { get; } = catalogUrl;

    /// <inheritdoc />
    public async Task<CatalogIndex> GetIndexAsync(CancellationToken cancellationToken = default)
    {
        return await HttpClient.GetFromJsonAsync(CatalogUrl, NuGetJsonContext.Default.CatalogIndex, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CatalogPage> GetPageAsync(string pageUrl, CancellationToken cancellationToken = default)
    {
        return await HttpClient.GetFromJsonAsync(pageUrl, NuGetJsonContext.Default.CatalogPage, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PackageDeleteCatalogLeaf> GetPackageDeleteLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
    {
        return await GetAndValidateLeafAsync(
            "PackageDelete",
            leafUrl,
            NuGetJsonContext.Default.PackageDeleteCatalogLeaf,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PackageDetailsCatalogLeaf> GetPackageDetailsLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
    {
        return await GetAndValidateLeafAsync(
            "PackageDetails",
            leafUrl,
            NuGetJsonContext.Default.PackageDetailsCatalogLeaf,
            cancellationToken);
    }

    private async Task<TCatalogLeaf> GetAndValidateLeafAsync<TCatalogLeaf>(string leafType, string leafUrl, JsonTypeInfo<TCatalogLeaf> jsonTypeInfo, CancellationToken cancellationToken) where TCatalogLeaf : CatalogLeaf
    {
        TCatalogLeaf leaf = await HttpClient.GetFromJsonAsync(leafUrl, jsonTypeInfo, cancellationToken);

        if (leaf.Type is not [string type, ..] || type != leafType)
        {
            throw new ArgumentException(
                $"The leaf type found in the document does not match the expected '{leafType}' type.",
                nameof(leafType));
        }

        return leaf;
    }
}
