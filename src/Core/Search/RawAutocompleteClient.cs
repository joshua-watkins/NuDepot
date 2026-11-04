using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.Serialization;

namespace NuDepot.Core.Search;

/// <summary>
/// The client used to search for packages.
/// 
/// See https://docs.microsoft.com/en-us/nuget/api/search-autocomplete-service-resource
/// </summary>
/// <param name="httpClient">The HTTP client used to send requests.</param>
/// <param name="autocompleteUrl">The NuGet server's autocomplete URL.</param>
public class RawAutocompleteClient(HttpClient httpClient, string autocompleteUrl) : IAutocompleteClient
{
    private HttpClient HttpClient { get; } = httpClient;
    private string AutocompleteUrl { get; } = autocompleteUrl;

    /// <inheritdoc />
    public async Task<AutocompleteResponse> AutocompleteAsync(string? query = null, int skip = 0, int take = 20, bool includePrerelease = true, bool includeSemVer2 = true, CancellationToken cancellationToken = default)
    {
        string url = RawSearchClient.AddSearchQueryString(
            AutocompleteUrl,
            query,
            skip,
            take,
            includePrerelease,
            includeSemVer2,
            "q");

        return await HttpClient.GetFromJsonAsync(url, NuGetJsonContext.Default.AutocompleteResponse, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AutocompleteResponse> ListPackageVersionsAsync(string packageId, bool includePrerelease = true, bool includeSemVer2 = true, CancellationToken cancellationToken = default)
    {
        string url = RawSearchClient.AddSearchQueryString(
            AutocompleteUrl,
            packageId,
            skip: null,
            take: null,
            includePrerelease,
            includeSemVer2,
            "id");

        return await HttpClient.GetFromJsonAsync(url, NuGetJsonContext.Default.AutocompleteResponse, cancellationToken);
    }
}
