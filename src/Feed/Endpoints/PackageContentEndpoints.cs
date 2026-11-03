using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NuGet.Core.Serialization;
using NuGet.Feed.Content;
using NuGet.Versioning;

namespace NuGet.Feed.Endpoints;

/// <summary>
/// The Package Content resource, used to download content from packages.
/// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
/// </summary>
internal static class PackageContentEndpoints
{
    // GET v3/package/{id}/index.json
    public static async Task<IResult> GetPackageVersionsAsync(string id, IPackageContentService content, CancellationToken cancellationToken)
    {
        var versions = await content.GetPackageVersionsOrNullAsync(id, cancellationToken);
        if (versions == null)
        {
            return Results.NotFound();
        }

        return Results.Json(versions, NuGetJsonContext.Default.PackageVersionsResponse);
    }

    // GET v3/package/{id}/{version}/{id}.{version}.nupkg
    public static Task<IResult> DownloadPackageAsync(string id, string version, IPackageContentService content, CancellationToken cancellationToken)
        => DownloadAsync(version, "application/octet-stream", v => content.GetPackageContentStreamOrNullAsync(id, v, cancellationToken));

    // GET v3/package/{id}/{version}/{id}.nuspec
    public static Task<IResult> DownloadNuspecAsync(string id, string version, IPackageContentService content, CancellationToken cancellationToken)
        => DownloadAsync(version, "text/xml", v => content.GetPackageManifestStreamOrNullAsync(id, v, cancellationToken));

    // GET v3/package/{id}/{version}/readme
    public static Task<IResult> DownloadReadmeAsync(string id, string version, IPackageContentService content, CancellationToken cancellationToken)
        => DownloadAsync(version, "text/markdown", v => content.GetPackageReadmeStreamOrNullAsync(id, v, cancellationToken));

    // GET v3/package/{id}/{version}/icon
    public static Task<IResult> DownloadIconAsync(string id, string version, IPackageContentService content, CancellationToken cancellationToken)
        => DownloadAsync(version, "image/xyz", v => content.GetPackageIconStreamOrNullAsync(id, v, cancellationToken));

    // GET v3/package/{id}/{version}/license.txt
    public static Task<IResult> DownloadLicenseTextAsync(string id, string version, IPackageContentService content, CancellationToken cancellationToken)
        => DownloadAsync(version, "text/plain", v => content.GetPackageLicenseStreamOrNullAsync(id, v, cancellationToken));

    // GET v3/package/{id}/{version}/license.md
    public static Task<IResult> DownloadLicenseMarkdownAsync(string id, string version, IPackageContentService content, CancellationToken cancellationToken)
        => DownloadAsync(version, "text/markdown", v => content.GetPackageLicenseStreamOrNullAsync(id, v, cancellationToken));

    private static async Task<IResult> DownloadAsync(string version, string contentType, Func<NuGetVersion, Task<Stream?>> getStreamOrNullAsync)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return Results.NotFound();
        }

        var stream = await getStreamOrNullAsync(nugetVersion);
        if (stream == null)
        {
            return Results.NotFound();
        }

        return Results.Stream(stream, contentType);
    }
}
