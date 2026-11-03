using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NuGet.Feed.Indexing;
using NuGet.Feed.Metadata;
using NuGet.Feed.Serialization;
using NuGet.Versioning;

namespace NuGet.Feed.Endpoints;

/// <summary>
/// Deprecates package versions. This isn't part of the NuGet protocol; deprecations are
/// published to clients through the package metadata, search and catalog resources.
/// </summary>
internal static class PackageDeprecationEndpoints
{
    // PUT api/package/{id}/{version}/deprecation
    public static async Task<IResult> DeprecateAsync(
        string id,
        string version,
        HttpRequest request,
        IPackageDeprecationService deprecations,
        CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion)) return Results.NotFound();

        var body = await EndpointHelpers.ReadJsonOrNullAsync(request, AdminJsonContext.Default.DeprecationRequest, cancellationToken);

        var reasons = new List<string>();
        foreach (var reason in body?.Reasons ?? Array.Empty<string>())
        {
            var known = PackageModelExtensions.DeprecationReasons
                .FirstOrDefault(r => string.Equals(r, reason, StringComparison.OrdinalIgnoreCase));
            if (known == null)
            {
                return EndpointHelpers.Text(
                    StatusCodes.Status400BadRequest,
                    $"Unknown deprecation reason '{reason}'. Use: {string.Join(", ", PackageModelExtensions.DeprecationReasons)}.");
            }

            if (!reasons.Contains(known)) reasons.Add(known);
        }

        if (body == null || reasons.Count == 0)
        {
            return EndpointHelpers.Text(
                StatusCodes.Status400BadRequest,
                $"At least one deprecation reason is required: {string.Join(", ", PackageModelExtensions.DeprecationReasons)}.");
        }

        var alternateId = string.IsNullOrWhiteSpace(body.AlternatePackageId) ? null : body.AlternatePackageId.Trim();
        var alternateRange = string.IsNullOrWhiteSpace(body.AlternatePackageVersionRange) ? null : body.AlternatePackageVersionRange.Trim();
        if (alternateRange != null && !VersionRange.TryParse(alternateRange, out _))
        {
            return EndpointHelpers.Text(StatusCodes.Status400BadRequest, $"'{alternateRange}' isn't a valid version range.");
        }

        var found = await deprecations.DeprecateAsync(
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
    public static async Task<IResult> UndeprecateAsync(
        string id,
        string version,
        IPackageDeprecationService deprecations,
        CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion)) return Results.NotFound();

        return await deprecations.UndeprecateAsync(id, nugetVersion, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
}
