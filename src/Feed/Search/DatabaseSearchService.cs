using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;
using NuGet.Core.Models;
using NuGet.Feed.Entities;
using NuGet.Feed.Indexing;
using NuGet.Feed.Metadata;
using NuGet.Feed.Vulnerabilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.Feed.Search;

/// <summary>
/// Searches the package database. Filters like prerelease and package type run in the database;
/// text matching and relevance ranking run in memory over each matching package's latest version.
/// </summary>
public class DatabaseSearchService : ISearchService
{
    private readonly NuGetFeedContext _context;
    private readonly IFrameworkCompatibilityService _frameworks;
    private readonly IVulnerabilityService _vulnerabilities;
    private readonly ISearchResponseBuilder _searchBuilder;

    public DatabaseSearchService(
        NuGetFeedContext context,
        IFrameworkCompatibilityService frameworks,
        IVulnerabilityService vulnerabilities,
        ISearchResponseBuilder searchBuilder)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _frameworks = frameworks ?? throw new ArgumentNullException(nameof(frameworks));
        _vulnerabilities = vulnerabilities ?? throw new ArgumentNullException(nameof(vulnerabilities));
        _searchBuilder = searchBuilder ?? throw new ArgumentNullException(nameof(searchBuilder));
    }

    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var frameworks = GetCompatibleFrameworksOrNull(request.Framework);
        var query = SearchQuery.Parse(request.Query);

        IQueryable<Package> search = ApplySearchFilters(
            _context.Packages,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            request.PackageType,
            frameworks);

        var candidates = await search
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

        var pageIds = ranked
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(p => p.Id)
            .ToList();

        // Fetch every version of the packages on this page so each result's latest version is correct.
        var packages = await ApplySearchFilters(
                _context.Packages.Include(p => p.PackageTypes).Where(p => pageIds.Contains(p.Id)),
                request.IncludePrerelease,
                request.IncludeSemVer2,
                request.PackageType,
                frameworks)
            .ToListAsync(cancellationToken);

        var vulnerabilities = await _vulnerabilities.FindAsync(pageIds, cancellationToken);
        var packagesById = packages
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var registrations = pageIds
            .Where(packagesById.ContainsKey)
            .Select(id => new PackageRegistration(
                packagesById[id][0].Id,
                packagesById[id],
                request.IncludeSemVer2,
                vulnerabilities: vulnerabilities.Where(v => string.Equals(v.PackageId, id, StringComparison.OrdinalIgnoreCase)).ToList()))
            .ToList();

        return _searchBuilder.BuildSearch(registrations, ranked.Count, request.IncludeSemVer2);
    }

    public async Task<AutocompleteResponse> AutocompleteAsync(
        AutocompleteRequest request,
        CancellationToken cancellationToken)
    {
        IQueryable<Package> search = _context.Packages;

        if (!string.IsNullOrEmpty(request.Query))
        {
            var term = request.Query.ToLowerInvariant();
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
        var prefix = request.Query ?? string.Empty;
        var packageIds = packages
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Id = g.First().Id, Downloads = g.Sum(p => p.Downloads) })
            .OrderByDescending(p => p.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(p => p.Downloads)
            .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.Id)
            .ToList();

        return _searchBuilder.BuildAutocomplete(
            packageIds.Skip(request.Skip).Take(request.Take).ToList(),
            packageIds.Count);
    }

    public async Task<AutocompleteResponse> ListPackageVersionsAsync(
        VersionsRequest request,
        CancellationToken cancellationToken)
    {
        var packageId = request.PackageId.ToLower();
        var search = _context
            .Packages
            .Where(p => p.Id.ToLower().Equals(packageId));

        search = ApplySearchFilters(
            search,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            packageType: null,
            frameworks: null);

        var packageVersions = await search
            .Select(p => p.NormalizedVersionString)
            .ToListAsync(cancellationToken);

        var sorted = packageVersions
            .OrderBy(v => NuGetVersion.Parse(v))
            .ToList();

        return _searchBuilder.BuildAutocomplete(sorted, sorted.Count);
    }

    public async Task<DependentsResponse> FindDependentsAsync(string packageId, CancellationToken cancellationToken)
    {
        var dependents = await _context
            .Packages
            .Where(p => p.Listed)
            .OrderByDescending(p => p.Downloads)
            .Where(p => p.Dependencies.Any(d => d.Id == packageId))
            .Take(20)
            .Select(r => new PackageDependent
            {
                Id = r.Id,
                Description = r.Description,
                TotalDownloads = r.Downloads
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        return _searchBuilder.BuildDependents(dependents);
    }

    /// <summary>
    /// Scores how well a package matches the query, or returns null if it doesn't match.
    /// </summary>
    private static int? Score(SearchQuery query, SearchCandidate latest, IReadOnlyList<SearchCandidate> versions)
    {
        foreach (var filter in query.Filters)
        {
            if (!MatchesFilter(filter, latest, versions)) return null;
        }

        // Without free text, rank by downloads alone.
        var score = 0;
        foreach (var term in query.Terms)
        {
            var termScore = ScoreTerm(term, latest);
            if (termScore == 0) return null;

            score += termScore;
        }

        return score;
    }

    private static bool MatchesFilter(SearchFilter filter, SearchCandidate latest, IReadOnlyList<SearchCandidate> versions)
    {
        var value = filter.Value;

        return filter.Field switch
        {
            "id" => Contains(latest.Id, value),
            "packageid" => string.Equals(latest.Id, value, StringComparison.OrdinalIgnoreCase),
            "version" => NuGetVersion.TryParse(value, out var version) && versions.Any(v => v.Version == version),
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
        var score = 0;

        if (string.Equals(package.Id, term, StringComparison.OrdinalIgnoreCase)) score += 1000;
        else if (package.Id.StartsWith(term, StringComparison.OrdinalIgnoreCase)) score += 400;
        else if (package.Id.Split('.', '-', '_').Any(part => string.Equals(part, term, StringComparison.OrdinalIgnoreCase))) score += 300;
        else if (Contains(package.Id, term)) score += 150;

        if (Contains(package.Title, term)) score += 80;
        if (package.Tags?.Any(t => string.Equals(t, term, StringComparison.OrdinalIgnoreCase)) == true) score += 60;
        if (package.Authors?.Any(a => Contains(a, term)) == true) score += 25;
        if (Contains(package.Summary, term)) score += 15;
        if (Contains(package.Description, term)) score += 10;

        return score;
    }

    private static bool Contains(string? value, string term)
        => value != null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private IQueryable<Package> ApplySearchFilters(
        IQueryable<Package> query,
        bool includePrerelease,
        bool includeSemVer2,
        string? packageType,
        IReadOnlyList<string>? frameworks)
    {
        if (!includePrerelease)
        {
            query = query.Where(p => !p.IsPrerelease);
        }

        if (!includeSemVer2)
        {
            query = query.Where(p => p.SemVerLevel != SemVerLevel.SemVer2);
        }

        if (!string.IsNullOrEmpty(packageType))
        {
            query = query.Where(p => p.PackageTypes.Any(t => t.Name == packageType));
        }

        if (frameworks != null)
        {
            query = query.Where(p => p.TargetFrameworks.Any(f => f.Moniker != null && frameworks.Contains(f.Moniker)));
        }

        return query.Where(p => p.Listed);
    }

    private IReadOnlyList<string>? GetCompatibleFrameworksOrNull(string? framework)
    {
        if (framework == null) return null;

        return _frameworks.FindAllCompatibleFrameworks(framework);
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
