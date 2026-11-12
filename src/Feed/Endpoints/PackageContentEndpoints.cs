using Microsoft.AspNetCore.Http;
using NuDepot.Core.Models;
using NuDepot.Core.Serialization;
using NuDepot.Feed.Content;
using NuGet.Versioning;

namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The Package Content resource, used to download content from packages.
/// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
/// </summary>
internal static class PackageContentEndpoints
{
    // GET v3/package/{id}/index.json
    public static async Task<IResult> GetPackageVersionsAsync(string id, IPackageContentService content, CancellationToken cancellationToken)
    {
        PackageVersionsResponse? versions = await content.GetPackageVersionsOrNullAsync(id, cancellationToken);
        if (versions == null)
            return Results.NotFound();

        return Results.Json(versions, NuGetJsonContext.Default.PackageVersionsResponse);
    }

    // GET v3/package/{id}/{version}/{id}.{version}.nupkg
    public static Task<IResult> DownloadPackageAsync(string id, string version, HttpResponse response, IPackageContentService content, CancellationToken cancellationToken)
        => DownloadAsync(response, version, _ => "application/octet-stream", v => content.GetPackageContentStreamOrNullAsync(id, v, cancellationToken));

    // GET v3/package/{id}/{version}/{id}.nuspec
    public static Task<IResult> DownloadNuspecAsync(string id, string version, HttpResponse response, IPackageContentService content, CancellationToken cancellationToken)
        => DownloadAsync(response, version, _ => "text/xml", v => content.GetPackageManifestStreamOrNullAsync(id, v, cancellationToken));

    // GET v3/package/{id}/{version}/readme
    public static Task<IResult> DownloadReadmeAsync(string id, string version, HttpResponse response, IPackageContentService content, CancellationToken cancellationToken)
        => DownloadAsync(response, version, _ => "text/markdown", v => content.GetPackageReadmeStreamOrNullAsync(id, v, cancellationToken));

    // GET v3/package/{id}/{version}/icon
    public static Task<IResult> DownloadIconAsync(string id, string version, HttpResponse response, IPackageContentService content, CancellationToken cancellationToken)
        => DownloadAsync(response, version, GetImageContentType, v => content.GetPackageIconStreamOrNullAsync(id, v, cancellationToken));

    // GET v3/package/{id}/{version}/license.txt
    public static Task<IResult> DownloadLicenseTextAsync(string id, string version, HttpResponse response, IPackageContentService content, CancellationToken cancellationToken)
        => DownloadAsync(response, version, _ => "text/plain", v => content.GetPackageLicenseStreamOrNullAsync(id, v, cancellationToken));

    // GET v3/package/{id}/{version}/license.md
    public static Task<IResult> DownloadLicenseMarkdownAsync(string id, string version, HttpResponse response, IPackageContentService content, CancellationToken cancellationToken)
        => DownloadAsync(response, version, _ => "text/markdown", v => content.GetPackageLicenseStreamOrNullAsync(id, v, cancellationToken));

    /// <summary>
    /// The policy of the files packages bring: their authors wrote them, and the gallery may share the feed's origin
    /// and its sign-in cookie, so a browser that opens one mustn't run it as part of the feed. It's sandboxed in an
    /// origin of its own, without scripts.
    /// </summary>
    internal const string PackageContentPolicy = "default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; sandbox";

    private static async Task<IResult> DownloadAsync(HttpResponse response, string version, Func<Stream, string> getContentType, Func<NuGetVersion, Task<Stream?>> getStreamOrNullAsync)
    {
        if (!NuGetVersion.TryParse(version, out NuGetVersion? nugetVersion))
            return Results.NotFound();

        Stream? stream = await getStreamOrNullAsync(nugetVersion);
        if (stream == null)
            return Results.NotFound();

        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.ContentSecurityPolicy = PackageContentPolicy;

        return Results.Stream(stream, getContentType(stream));
    }

    // An icon's type, from its first bytes: NuGet allows PNG and JPEG icons, and older packages have others. Anything
    // else, like SVG or HTML, is only downloaded.
    internal static string GetImageContentType(Stream stream)
    {
        if (!stream.CanSeek)
            return "application/octet-stream";

        Span<byte> header = stackalloc byte[12];
        long start = stream.Position;
        int read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        stream.Position = start;
        header = header[..read];

        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G']))
            return "image/png";
        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            return "image/jpeg";
        if (header.StartsWith("GIF8"u8))
            return "image/gif";
        if (header.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0x01, 0x00]))
            return "image/x-icon";
        if (header.Length == 12 && header.StartsWith("RIFF"u8) && header[8..].SequenceEqual("WEBP"u8))
            return "image/webp";

        return "application/octet-stream";
    }
}
