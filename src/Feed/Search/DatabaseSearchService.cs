using Microsoft.EntityFrameworkCore;
using NuDepot.Core.Models;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Metadata;
using NuDepot.Feed.Vulnerabilities;
using NuGet.Versioning;

namespace NuDepot.Feed.Search;

/// <summary>
/// Searches the package database. Filters like prerelease and package type run in the database;
/// text matching and relevance ranking run in memory over each matching package's latest version.
/// </summary>
internal class DatabaseSearchService(NuDepotDbContext context, IFrameworkCompatibilityService frameworks, IVulnerabilityService vulnerabilities, ISearchResponseBuilder searchBuilder) : ISearchService
{
    /// <summary>
    /// The most dependents returned for a package, ordered by their total downloads.
    /// </summary>
    public const int MaxDependents = 20;

    private NuDepotDbContext Context { get; } = context;
    private IFrameworkCompatibilityService Frameworks { get; } = frameworks;
    private IVulnerabilityService Vulnerabilities { get; } = vulnerabilities;
    private ISearchResponseBuilder SearchBuilder { get; } = searchBuilder;

    /// <inheritdoc />
    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<string>? frameworks = GetCompatibleFrameworksOrNull(request.Framework);
        SearchQuery query = SearchQuery.Parse(request.Query);

        IQueryable<Package> search = ApplySearchFilters(
            Context.Packages,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            request.PackageType,
            frameworks);

        List<SearchCandidate> candidates = await search
            .Select(p => new SearchCandidate
            {
                Id = p.Id,
                NormalizedVersion = p.NormalizedVersionString,
                OriginalVersion = p.OriginalVersionString,
                Title = p.Title,
                Description = p.Description,
                Summary = p.Summary,
                Tags = p.Tags,
                Authors = p.Authors,
                Downloads = p.Downloads,
            })
            .ToListAsync(cancellationToken);

        var ranked = candidates
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Id = group.Key, Versions = group.ToList(), Latest = group.MaxBy(c => c.Version)! })
            .Select(p => new
            {
                p.Id,
                Score = Score(query, p.Latest, p.Versions),
                Downloads = p.Versions.Sum(v => v.Downloads),
            })
            .Where(p => p.Score.HasValue)
            .OrderByDescending(p => p.Score)
            .ThenByDescending(p => p.Downloads)
            .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<string> pageIds = [.. ranked
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(p => p.Id)];

        // Fetch every version of the packages on this page so each result's latest version is correct.
        List<Package> packages = await ApplySearchFilters(
                Context.Packages.Include(p => p.PackageTypes).Where(p => pageIds.Contains(p.Id)),
                request.IncludePrerelease,
                request.IncludeSemVer2,
                request.PackageType,
                frameworks)
            .ToListAsync(cancellationToken);

        IReadOnlyList<VulnerabilityAdvisory> vulnerabilities = await Vulnerabilities.FindAsync(pageIds, cancellationToken);
        Dictionary<string, List<Package>> packagesById = packages
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        List<PackageRegistration> registrations = [.. pageIds
            .Where(packagesById.ContainsKey)
            .Select(id => new PackageRegistration(
                packagesById[id][0].Id,
                packagesById[id],
                request.IncludeSemVer2,
                vulnerabilities: [.. vulnerabilities.Where(v => string.Equals(v.PackageId, id, StringComparison.OrdinalIgnoreCase))]))];

        return SearchBuilder.BuildSearch(registrations, ranked.Count, request.IncludeSemVer2);
    }

    /// <inheritdoc />
    public async Task<AutocompleteResponse> AutocompleteAsync(AutocompleteRequest request, CancellationToken cancellationToken)
    {
        IQueryable<Package> search = Context.Packages;

        if (!string.IsNullOrEmpty(request.Query))
        {
            string term = request.Query.ToLowerInvariant();
            search = search.Where(p => p.Id.ToLower().Contains(term));
        }

        search = ApplySearchFilters(
            search,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            request.PackageType,
            frameworks: null);

        var packages = await search
            .Select(p => new { p.Id, p.Downloads })
            .ToListAsync(cancellationToken);

        // IDs that start with the query rank above IDs that only contain it.
        string prefix = request.Query ?? string.Empty;
        List<string> packageIds = [.. packages
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { g.First().Id, Downloads = g.Sum(p => p.Downloads) })
            .OrderByDescending(p => p.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(p => p.Downloads)
            .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.Id)];

        return SearchBuilder.BuildAutocomplete(
            [.. packageIds.Skip(request.Skip).Take(request.Take)],
            packageIds.Count);
    }

    /// <inheritdoc />
    public async Task<AutocompleteResponse> ListPackageVersionsAsync(VersionsRequest request, CancellationToken cancellationToken)
    {
        string packageId = request.PackageId.ToLower();
        IQueryable<Package> search = Context
            .Packages
            .Where(p => p.Id.ToLower().Equals(packageId));

        search = ApplySearchFilters(
            search,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            packageType: null,
            frameworks: null);

        List<string> packageVersions = await search
            .Select(p => p.NormalizedVersionString)
            .ToListAsync(cancellationToken);

        List<string> sorted = [.. packageVersions.OrderBy(v => NuGetVersion.Parse(v))];

        return SearchBuilder.BuildAutocomplete(sorted, sorted.Count);
    }

    /// <inheritdoc />
    public async Task<DependentsResponse> FindDependentsAsync(string packageId, CancellationToken cancellationToken)
    {
        // A dependent is a package with a listed version that depends on the package. Its downloads
        // are the total across all its versions, and its description is its latest listed version's.
        IQueryable<string> dependentIds = Context.Packages
            .Where(p => p.Listed && p.Dependencies.Any(d => d.Id == packageId))
            .Select(p => p.Id);

        var dependents = await Context.Packages
            .Where(p => dependentIds.Contains(p.Id))
            .GroupBy(p => p.Id)
            .Select(g => new
            {
                Id = g.Key,
                TotalDownloads = g.Sum(p => p.Downloads),
                LatestListedKey = g.Where(p => p.Listed).Max(p => (int?)p.Key),
            })
            .OrderByDescending(d => d.TotalDownloads)
            .ThenBy(d => d.Id)
            .Take(MaxDependents)
            .ToListAsync(cancellationToken);

        List<int?> latestKeys = [.. dependents.Select(d => d.LatestListedKey)];
        Dictionary<int, string?> descriptions = await Context.Packages
            .Where(p => latestKeys.Contains(p.Key))
            .ToDictionaryAsync(p => p.Key, p => p.Description, cancellationToken);

        return SearchBuilder.BuildDependents([.. dependents
            .Select(d => new PackageDependent
            {
                Id = d.Id,
                Description = d.LatestListedKey is int key ? descriptions.GetValueOrDefault(key) : null,
                TotalDownloads = d.TotalDownloads,
            })]);
    }

    /// <summary>
    /// Scores how well a package matches the query, or returns null if it doesn't match.
    /// </summary>
    private static int? Score(SearchQuery query, SearchCandidate latest, IReadOnlyList<SearchCandidate> versions)
    {
        foreach (SearchFilter filter in query.Filters)
        {
            if (!MatchesFilter(filter, latest, versions))
                return null;
        }

        // Without free text, rank by downloads alone.
        int score = 0;
        foreach (string term in query.Terms)
        {
            int termScore = ScoreTerm(term, latest);
            if (termScore == 0)
                return null;

            score += termScore;
        }

        return score;
    }

    private static bool MatchesFilter(SearchFilter filter, SearchCandidate latest, IReadOnlyList<SearchCandidate> versions)
    {
        string value = filter.Value;

        return filter.Field switch
        {
            "id" => Contains(latest.Id, value),
            "packageid" => string.Equals(latest.Id, value, StringComparison.OrdinalIgnoreCase),
            "version" => NuGetVersion.TryParse(value, out NuGetVersion? version) && versions.Any(v => v.Version == version),
            "title" => Contains(latest.Title, value),
            "tags" => latest.Tags?.Any(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase)) == true,
            "authors" => latest.Authors?.Any(a => Contains(a, value)) == true,
            "description" => Contains(latest.Description, value),
            "summary" => Contains(latest.Summary, value),
            _ => false,
        };
    }

    private static int ScoreTerm(string term, SearchCandidate package)
    {
        int score = 0;

        if (string.Equals(package.Id, term, StringComparison.OrdinalIgnoreCase))
            score += 1000;
        else if (package.Id.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            score += 400;
        else if (package.Id.Split('.', '-', '_').Any(part => string.Equals(part, term, StringComparison.OrdinalIgnoreCase)))
            score += 300;
        else if (Contains(package.Id, term))
            score += 150;

        if (Contains(package.Title, term))
            score += 80;
        if (package.Tags?.Any(t => string.Equals(t, term, StringComparison.OrdinalIgnoreCase)) == true)
            score += 60;
        if (package.Authors?.Any(a => Contains(a, term)) == true)
            score += 25;
        if (Contains(package.Summary, term))
            score += 15;
        if (Contains(package.Description, term))
            score += 10;

        return score;
    }

    private static bool Contains(string? value, string term)
        => value != null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static IQueryable<Package> ApplySearchFilters(IQueryable<Package> query, bool includePrerelease, bool includeSemVer2, string? packageType, IReadOnlyList<string>? frameworks)
    {
        if (!includePrerelease)
            query = query.Where(p => !p.IsPrerelease);

        if (!includeSemVer2)
            query = query.Where(p => p.SemVerLevel != SemVerLevel.SemVer2);

        if (!string.IsNullOrEmpty(packageType))
            query = query.Where(p => p.PackageTypes.Any(t => t.Name == packageType));

        if (frameworks != null)
            query = query.Where(p => p.TargetFrameworks.Any(f => f.Moniker != null && frameworks.Contains(f.Moniker)));

        return query.Where(p => p.Listed);
    }

    private IReadOnlyList<string>? GetCompatibleFrameworksOrNull(string? framework)
    {
        if (framework == null)
            return null;

        return Frameworks.FindAllCompatibleFrameworks(framework);
    }

    private class SearchCandidate
    {
        public string Id { get; set; } = null!;
        public string NormalizedVersion { get; set; } = null!;
        public string? OriginalVersion { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Summary { get; set; }
        public string[]? Tags { get; set; }
        public string[]? Authors { get; set; }
        public long Downloads { get; set; }

        public NuGetVersion Version => NuGetVersion.Parse(OriginalVersion ?? NormalizedVersion);
    }
}
