using NuDepot.Core.Models;

namespace NuDepot.Feed.Search;

/// <summary>
/// A minimal search service implementation, used for advanced scenarios.
/// </summary>
public class NullSearchService : ISearchService
{
    private static readonly IReadOnlyList<string> EmptyStringList = [];

    private static readonly Task<AutocompleteResponse> EmptyAutocompleteResponseTask =
        Task.FromResult(new AutocompleteResponse
        {
            TotalHits = 0,
            Data = EmptyStringList,
            Context = AutocompleteContext.Default
        });

    private static readonly Task<DependentsResponse> EmptyDependentsResponseTask =
        Task.FromResult(new DependentsResponse
        {
            TotalHits = 0,
            Data = []
        });

    private static readonly Task<SearchResponse> EmptySearchResponseTask =
        Task.FromResult(new SearchResponse
        {
            TotalHits = 0,
            Data = []
        });

    /// <inheritdoc />
    public Task<AutocompleteResponse> AutocompleteAsync(AutocompleteRequest request, CancellationToken cancellationToken)
    {
        return EmptyAutocompleteResponseTask;
    }

    /// <inheritdoc />
    public Task<AutocompleteResponse> ListPackageVersionsAsync(VersionsRequest request, CancellationToken cancellationToken)
    {
        return EmptyAutocompleteResponseTask;
    }

    /// <inheritdoc />
    public Task<DependentsResponse> FindDependentsAsync(string packageId, CancellationToken cancellationToken)
    {
        return EmptyDependentsResponseTask;
    }

    /// <inheritdoc />
    public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        return EmptySearchResponseTask;
    }
}
