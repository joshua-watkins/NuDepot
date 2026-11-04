using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Storage;
using NuGet.Versioning;

namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The Package Publish and Symbol Package Publish resources, used to push, delete and relist packages.
/// See: https://docs.microsoft.com/en-us/nuget/api/package-publish-resource
/// </summary>
internal static class PackagePublishEndpoints
{
    private const string LoggerCategory = "NuDepot.Feed.Endpoints.PackagePublish";

    // PUT api/v2/package
    // See: https://docs.microsoft.com/en-us/nuget/api/package-publish-resource#push-a-package
    public static async Task<IResult> UploadPackageAsync(HttpRequest request, FeedAccess access, IPackageIndexingService indexer, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        try
        {
            using Stream? uploadStream = await request.GetUploadStreamOrNullAsync(cancellationToken);
            if (uploadStream == null)
                return Results.StatusCode(StatusCodes.Status400BadRequest);

            // Replacing a package deletes the original permanently.
            bool allowOverwrite = access.HasPermission(request.HttpContext.User, FeedPermissions.HardDelete);
            PackageIndexingResult result = await indexer.IndexAsync(uploadStream, allowOverwrite, cancellationToken);

            return Results.StatusCode(result switch
            {
                PackageIndexingResult.InvalidPackage => StatusCodes.Status400BadRequest,
                PackageIndexingResult.PackageAlreadyExists => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status201Created,
            });
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            loggerFactory
                .CreateLogger(LoggerCategory)
                .LogError(e, "Exception thrown during package upload");

            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    // DELETE api/v2/package/{id}/{version}
    public static async Task<IResult> DeleteAsync(string id, string version, IPackageDeletionService deletionService, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out NuGetVersion? nugetVersion))
            return Results.NotFound();

        return await deletionService.TryDeletePackageAsync(id, nugetVersion, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }

    // POST api/v2/package/{id}/{version}
    public static async Task<IResult> RelistAsync(string id, string version, IPackageDeletionService deletionService, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out NuGetVersion? nugetVersion))
            return Results.NotFound();

        return await deletionService.TryRelistPackageAsync(id, nugetVersion, cancellationToken)
            ? Results.Ok()
            : Results.NotFound();
    }

    // PUT api/v2/symbol
    // See: https://docs.microsoft.com/en-us/nuget/api/symbol-package-publish-resource
    public static async Task<IResult> UploadSymbolPackageAsync(HttpRequest request, ISymbolIndexingService indexer, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        try
        {
            using Stream? uploadStream = await request.GetUploadStreamOrNullAsync(cancellationToken);
            if (uploadStream == null)
                return Results.StatusCode(StatusCodes.Status400BadRequest);

            SymbolIndexingResult result = await indexer.IndexAsync(uploadStream, cancellationToken);

            return Results.StatusCode(result switch
            {
                SymbolIndexingResult.InvalidSymbolPackage => StatusCodes.Status400BadRequest,
                SymbolIndexingResult.PackageNotFound => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status201Created,
            });
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            loggerFactory
                .CreateLogger(LoggerCategory)
                .LogError(e, "Exception thrown during symbol upload");

            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    // GET api/download/symbols/{file}/{key}/{file2}
    public static async Task<IResult> DownloadSymbolsAsync(string file, string key, ISymbolStorageService storage)
    {
        Stream? pdbStream = await storage.GetPortablePdbContentStreamOrNullAsync(file, key);
        if (pdbStream == null)
            return Results.NotFound();

        return Results.Stream(pdbStream, "application/octet-stream");
    }
}
