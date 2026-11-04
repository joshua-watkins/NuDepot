using Microsoft.AspNetCore.Http;
using NuDepot.Core.Models;
using NuDepot.Core.Serialization;
using NuDepot.Feed.Catalog;

namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The Catalog resource, a record of every change to the feed's packages.
/// See: https://learn.microsoft.com/en-us/nuget/api/catalog-resource
/// </summary>
internal static class CatalogEndpoints
{
    // GET v3/catalog/index.json
    public static async Task<IResult> IndexAsync(ICatalogService catalog, CancellationToken cancellationToken)
    {
        CatalogIndex index = await catalog.GetIndexAsync(cancellationToken);

        return Results.Json(index, NuGetJsonContext.Default.CatalogIndex);
    }

    // GET v3/catalog/page{page}.json
    public static async Task<IResult> PageAsync(int page, ICatalogService catalog, CancellationToken cancellationToken)
    {
        CatalogPage? result = await catalog.GetPageOrNullAsync(page, cancellationToken);
        if (result == null)
            return Results.NotFound();

        return Results.Json(result, NuGetJsonContext.Default.CatalogPage);
    }

    // GET v3/catalog/data/{key}/{id}.{version}.json
    public static async Task<IResult> LeafAsync(int key, ICatalogService catalog, CancellationToken cancellationToken)
    {
        // Serialize the leaf as its runtime type: package details or package delete.
        return await catalog.GetLeafOrNullAsync(key, cancellationToken) switch
        {
            PackageDetailsCatalogLeaf details => Results.Json(details, NuGetJsonContext.Default.PackageDetailsCatalogLeaf),
            PackageDeleteCatalogLeaf delete => Results.Json(delete, NuGetJsonContext.Default.PackageDeleteCatalogLeaf),
            _ => Results.NotFound(),
        };
    }
}
