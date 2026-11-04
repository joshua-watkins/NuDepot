using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.Serialization;

namespace NuDepot.Core.Catalog;

public class RawCatalogClient : ICatalogClient
{
    private readonly HttpClient _httpClient;
    private readonly string _catalogUrl;

    public RawCatalogClient(HttpClient httpClient, string catalogUrl)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _catalogUrl = catalogUrl ?? throw new ArgumentNullException(nameof(catalogUrl));
    }

    public async Task<CatalogIndex> GetIndexAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync(_catalogUrl, NuGetJsonContext.Default.CatalogIndex, cancellationToken);
    }

    public async Task<CatalogPage> GetPageAsync(string pageUrl, CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync(pageUrl, NuGetJsonContext.Default.CatalogPage, cancellationToken);
    }

    public async Task<PackageDeleteCatalogLeaf> GetPackageDeleteLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
    {
        return await GetAndValidateLeafAsync(
            "PackageDelete",
            leafUrl,
            NuGetJsonContext.Default.PackageDeleteCatalogLeaf,
            cancellationToken);
    }

    public async Task<PackageDetailsCatalogLeaf> GetPackageDetailsLeafAsync(string leafUrl, CancellationToken cancellationToken = default)
    {
        return await GetAndValidateLeafAsync(
            "PackageDetails",
            leafUrl,
            NuGetJsonContext.Default.PackageDetailsCatalogLeaf,
            cancellationToken);
    }

    private async Task<TCatalogLeaf> GetAndValidateLeafAsync<TCatalogLeaf>(
        string leafType,
        string leafUrl,
        JsonTypeInfo<TCatalogLeaf> jsonTypeInfo,
        CancellationToken cancellationToken) where TCatalogLeaf : CatalogLeaf
    {
        var leaf = await _httpClient.GetFromJsonAsync(leafUrl, jsonTypeInfo, cancellationToken);

        if (leaf.Type?.FirstOrDefault() != leafType)
        {
            throw new ArgumentException(
                $"The leaf type found in the document does not match the expected '{leafType}' type.",
                nameof(leafType));
        }

        return leaf;
    }
}
