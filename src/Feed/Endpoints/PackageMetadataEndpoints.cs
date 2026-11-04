using Microsoft.AspNetCore.Http;
using NuDepot.Core.Models;
using NuDepot.Core.Serialization;
using NuDepot.Feed.Metadata;
using NuGet.Versioning;

namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The Package Metadata resource, used to fetch packages' information.
/// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
/// </summary>
internal static class PackageMetadataEndpoints
{
    // GET v3/registration/{id}/index.json
    public static Task<IResult> RegistrationIndexAsync(string id, IPackageMetadataService metadata, CancellationToken cancellationToken)
        => GetRegistrationIndexAsync(id, semVer2: false, metadata, cancellationToken);

    // GET v3/registration-semver2/{id}/index.json
    public static Task<IResult> SemVer2RegistrationIndexAsync(string id, IPackageMetadataService metadata, CancellationToken cancellationToken)
        => GetRegistrationIndexAsync(id, semVer2: true, metadata, cancellationToken);

    // GET v3/registration/{id}/{version}.json
    public static Task<IResult> RegistrationLeafAsync(string id, string version, IPackageMetadataService metadata, CancellationToken cancellationToken)
        => GetRegistrationLeafAsync(id, version, semVer2: false, metadata, cancellationToken);

    // GET v3/registration-semver2/{id}/{version}.json
    public static Task<IResult> SemVer2RegistrationLeafAsync(string id, string version, IPackageMetadataService metadata, CancellationToken cancellationToken)
        => GetRegistrationLeafAsync(id, version, semVer2: true, metadata, cancellationToken);

    private static async Task<IResult> GetRegistrationIndexAsync(string id, bool semVer2, IPackageMetadataService metadata, CancellationToken cancellationToken)
    {
        NuDepotRegistrationIndexResponse? index = await metadata.GetRegistrationIndexOrNullAsync(id, semVer2, cancellationToken);
        if (index == null)
            return Results.NotFound();

        return Results.Json(index, NuGetJsonContext.Default.NuDepotRegistrationIndexResponse);
    }

    private static async Task<IResult> GetRegistrationLeafAsync(string id, string version, bool semVer2, IPackageMetadataService metadata, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out NuGetVersion? nugetVersion))
            return Results.NotFound();

        RegistrationLeafResponse? leaf = await metadata.GetRegistrationLeafOrNullAsync(id, nugetVersion, semVer2, cancellationToken);
        if (leaf == null)
            return Results.NotFound();

        return Results.Json(leaf, NuGetJsonContext.Default.RegistrationLeafResponse);
    }
}
