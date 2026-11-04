using Microsoft.AspNetCore.Http;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Metadata;
using NuDepot.Feed.Serialization;
using NuGet.Versioning;

namespace NuDepot.Feed.Endpoints;

/// <summary>
/// Deprecates package versions. This isn't part of the NuGet protocol; deprecations are
/// published to clients through the package metadata, search and catalog resources.
/// </summary>
internal static class PackageDeprecationEndpoints
{
    // PUT api/package/{id}/{version}/deprecation
    public static async Task<IResult> DeprecateAsync(string id, string version, HttpRequest request, IPackageDeprecationService deprecations, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out NuGetVersion? nugetVersion))
            return Results.NotFound();

        DeprecationRequest? body = await EndpointHelpers.ReadJsonOrNullAsync(request, AdminJsonContext.Default.DeprecationRequest, cancellationToken);

        List<string> reasons = [];
        foreach (string reason in body?.Reasons ?? [])
        {
            string? known = PackageModelExtensions.DeprecationReasons
                .FirstOrDefault(r => string.Equals(r, reason, StringComparison.OrdinalIgnoreCase));
            if (known == null)
            {
                return EndpointHelpers.Text(
                    StatusCodes.Status400BadRequest,
                    $"Unknown deprecation reason '{reason}'. Use: {string.Join(", ", PackageModelExtensions.DeprecationReasons)}.");
            }

            if (!reasons.Contains(known))
                reasons.Add(known);
        }

        if (body == null || reasons.Count == 0)
        {
            return EndpointHelpers.Text(
                StatusCodes.Status400BadRequest,
                $"At least one deprecation reason is required: {string.Join(", ", PackageModelExtensions.DeprecationReasons)}.");
        }

        string? alternateId = string.IsNullOrWhiteSpace(body.AlternatePackageId) ? null : body.AlternatePackageId.Trim();
        string? alternateRange = string.IsNullOrWhiteSpace(body.AlternatePackageVersionRange) ? null : body.AlternatePackageVersionRange.Trim();
        if (alternateRange != null && !VersionRange.TryParse(alternateRange, out _))
            return EndpointHelpers.Text(StatusCodes.Status400BadRequest, $"'{alternateRange}' isn't a valid version range.");

        bool found = await deprecations.DeprecateAsync(
            id,
            nugetVersion,
            reasons,
            string.IsNullOrWhiteSpace(body.Message) ? null : body.Message,
            alternateId,
            alternateRange,
            cancellationToken);

        return found ? Results.NoContent() : Results.NotFound();
    }

    // DELETE api/package/{id}/{version}/deprecation
    public static async Task<IResult> UndeprecateAsync(string id, string version, IPackageDeprecationService deprecations, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out NuGetVersion? nugetVersion))
            return Results.NotFound();

        return await deprecations.UndeprecateAsync(id, nugetVersion, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
}
