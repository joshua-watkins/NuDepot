using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NuDepot.Core.Models;
using NuDepot.Core.Serialization;
using NuDepot.Feed.Search;

namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The Search Query and Search Autocomplete resources.
/// See: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource
/// </summary>
internal static class SearchEndpoints
{
    /// <summary>
    /// The most results a request may ask for, as on nuget.org.
    /// </summary>
    public const int MaxTake = 1000;

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
        SearchRequest request = new()
        {
            Skip = Math.Max(skip, 0),
            Take = Math.Clamp(take, 0, MaxTake),
            IncludePrerelease = prerelease,
            IncludeSemVer2 = semVerLevel == "2.0.0",
            PackageType = packageType,
            Framework = framework,
            Query = query ?? string.Empty,
        };

        SearchResponse response = await searchService.SearchAsync(request, cancellationToken);

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
            VersionsRequest versionsRequest = new()
            {
                IncludePrerelease = prerelease,
                IncludeSemVer2 = semVerLevel == "2.0.0",
                PackageId = versionsQuery,
            };

            AutocompleteResponse versions = await searchService.ListPackageVersionsAsync(versionsRequest, cancellationToken);

            return Results.Json(versions, NuGetJsonContext.Default.AutocompleteResponse);
        }

        AutocompleteRequest request = new()
        {
            IncludePrerelease = prerelease,
            IncludeSemVer2 = semVerLevel == "2.0.0",
            PackageType = packageType,
            Skip = Math.Max(skip, 0),
            Take = Math.Clamp(take, 0, MaxTake),
            Query = autocompleteQuery,
        };

        AutocompleteResponse response = await searchService.AutocompleteAsync(request, cancellationToken);

        return Results.Json(response, NuGetJsonContext.Default.AutocompleteResponse);
    }

    // GET v3/dependents
    // This is an unofficial API to find packages that depend on a given package.
    public static async Task<IResult> DependentsAsync(ISearchService searchService, CancellationToken cancellationToken, string? packageId = null)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return Results.BadRequest();

        DependentsResponse response = await searchService.FindDependentsAsync(packageId, cancellationToken);

        return Results.Json(response, NuGetJsonContext.Default.DependentsResponse);
    }
}
