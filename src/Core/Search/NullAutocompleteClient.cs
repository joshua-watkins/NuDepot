using NuDepot.Core.Models;

namespace NuDepot.Core.Search;

/// <summary>
/// An autocomplete client for a feed without autocomplete. It never finds anything.
/// </summary>
public class NullAutocompleteClient : IAutocompleteClient
{
    /// <inheritdoc />
    public Task<AutocompleteResponse> AutocompleteAsync(string? query = null, int skip = 0, int take = 20, bool includePrerelease = true, bool includeSemVer2 = true, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AutocompleteResponse
        {
            TotalHits = 0,
            Data = []
        });
    }

    /// <inheritdoc />
    public Task<AutocompleteResponse> ListPackageVersionsAsync(string packageId, bool includePrerelease = true, bool includeSemVer2 = true, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AutocompleteResponse
        {
            TotalHits = 0,
            Data = []
        });
    }
}
