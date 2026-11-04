using NuDepot.Core.Models;
using NuDepot.Feed.Metadata;

namespace NuDepot.Feed.Search;

public interface ISearchResponseBuilder
{
    /// <param name="results">The page of matching packages.</param>
    /// <param name="totalHits">The number of matching packages across all pages.</param>
    /// <param name="includeSemVer2">Whether the search included SemVer 2.0.0 packages, which selects the registration hive to link to.</param>
    SearchResponse BuildSearch(IReadOnlyList<PackageRegistration> results, long totalHits, bool includeSemVer2);

    /// <param name="data">The page of results.</param>
    /// <param name="totalHits">The number of results across all pages.</param>
    AutocompleteResponse BuildAutocomplete(IReadOnlyList<string> data, long totalHits);

    DependentsResponse BuildDependents(IReadOnlyList<PackageDependent> results);
}
