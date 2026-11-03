using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NuGet.Core.Serialization;
using NuGet.Feed.Search;

namespace NuGet.Feed.Endpoints;

/// <summary>
/// The Search Query and Search Autocomplete resources.
/// See: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource
/// </summary>
internal static class SearchEndpoints
{
    // GET v3/search
    public static async Task<IResult> SearchAsync(
        ISearchService searchService,
        CancellationToken cancellationToken,
        [FromQuery(Name = "q")] string? query = null,
        int skip = 0,
        int take = 20,
        bool prerelease = false,
        string? semVerLevel = null,

        // These are unofficial parameters
        string? packageType = null,
        string? framework = null)
    {
        var request = new SearchRequest
        {
            Skip = skip,
            Take = take,
            IncludePrerelease = prerelease,
            IncludeSemVer2 = semVerLevel == "2.0.0",
            PackageType = packageType,
            Framework = framework,
            Query = query ?? string.Empty,
        };

        var response = await searchService.SearchAsync(request, cancellationToken);

        return Results.Json(response, NuGetJsonContext.Default.SearchResponse);
    }

    // GET v3/autocomplete
    public static async Task<IResult> AutocompleteAsync(
        ISearchService searchService,
        CancellationToken cancellationToken,
        [FromQuery(Name = "q")] string? autocompleteQuery = null,
        [FromQuery(Name = "id")] string? versionsQuery = null,
        bool prerelease = false,
        string? semVerLevel = null,
        int skip = 0,
        int take = 20,

        // These are unofficial parameters
        string? packageType = null)
    {
        // If only "id" is provided, find package versions. Otherwise, find package IDs.
        if (versionsQuery != null && autocompleteQuery == null)
        {
            var versionsRequest = new VersionsRequest
            {
                IncludePrerelease = prerelease,
                IncludeSemVer2 = semVerLevel == "2.0.0",
                PackageId = versionsQuery,
            };

            var versions = await searchService.ListPackageVersionsAsync(versionsRequest, cancellationToken);

            return Results.Json(versions, NuGetJsonContext.Default.AutocompleteResponse);
        }

        var request = new AutocompleteRequest
        {
            IncludePrerelease = prerelease,
            IncludeSemVer2 = semVerLevel == "2.0.0",
            PackageType = packageType,
            Skip = skip,
            Take = take,
            Query = autocompleteQuery,
        };

        var response = await searchService.AutocompleteAsync(request, cancellationToken);

        return Results.Json(response, NuGetJsonContext.Default.AutocompleteResponse);
    }

    // GET v3/dependents
    // This is an unofficial API to find packages that depend on a given package.
    public static async Task<IResult> DependentsAsync(
        ISearchService searchService,
        CancellationToken cancellationToken,
        string? packageId = null)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return Results.BadRequest();
        }

        var response = await searchService.FindDependentsAsync(packageId, cancellationToken);

        return Results.Json(response, NuGetJsonContext.Default.DependentsResponse);
    }
}
