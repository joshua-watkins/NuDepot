using NuDepot.Core.Models;
using NuDepot.Feed.Entities;
using NuGet.Versioning;
using System.Globalization;

namespace NuDepot.Feed.Metadata;

/// <summary>
/// Converts the package entity into the protocol's models. Shared by the package metadata,
/// search and catalog resources so they describe packages the same way.
/// </summary>
internal static class PackageModelExtensions
{
    /// <summary>
    /// The deprecation reasons the protocol defines.
    /// </summary>
    public static readonly IReadOnlyList<string> DeprecationReasons = ["Legacy", "CriticalBugs", "Other"];

    /// <summary>
    /// Whether the package version, or one of its dependencies, needs SemVer 2.0.0.
    /// </summary>
    /// <param name="package">The package version.</param>
    /// <returns>True if older clients can't see it.</returns>
    public static bool IsSemVer2(this Package package)
        => package.SemVerLevel == SemVerLevel.SemVer2 || package.Version.IsSemVer2;

    /// <summary>
    /// Groups the package's dependencies by target framework, as the protocol describes them.
    /// </summary>
    /// <param name="package">The package version, with its dependencies loaded.</param>
    /// <returns>The dependency groups. A framework without dependencies is a group without any.</returns>
    public static IReadOnlyList<DependencyGroupItem> ToDependencyGroups(this Package package)
    {
        return [.. (package.Dependencies ?? [])
            .GroupBy(d => d.TargetFramework)
            .Select(group => new DependencyGroupItem
            {
                TargetFramework = group.Key,

                // A package that supports a target framework but does not have dependencies while on
                // that target framework is represented by a fake dependency with a null "Id" and "VersionRange".
                // This fake dependency should not be included in the output.
                Dependencies = [.. group
                    .Where(d => d.Id != null && d.VersionRange != null)
                    .Select(d => new DependencyItem
                    {
                        Id = d.Id!,
                        Range = d.VersionRange
                    })]
            })];
    }

    /// <summary>
    /// Describes the package's deprecation, as the protocol does.
    /// </summary>
    /// <param name="package">The package version.</param>
    /// <returns>The deprecation, or null if the version isn't deprecated.</returns>
    public static PackageDeprecation? ToDeprecationOrNull(this Package package)
    {
        if (!package.IsDeprecated)
            return null;

        return new PackageDeprecation
        {
            Reasons = package.DeprecationReasons!,
            Message = string.IsNullOrEmpty(package.DeprecationMessage) ? null : package.DeprecationMessage,
            AlternatePackage = string.IsNullOrEmpty(package.AlternatePackageId)
                ? null
                : new AlternatePackage
                {
                    Id = package.AlternatePackageId,
                    Range = string.IsNullOrEmpty(package.AlternatePackageVersionRange) ? "*" : package.AlternatePackageVersionRange,
                },
        };
    }

    /// <summary>
    /// The advisories that affect the package's version, or null if there are none.
    /// </summary>
    public static List<PackageVulnerability>? ToVulnerabilitiesOrNull(this Package package, IEnumerable<VulnerabilityAdvisory>? advisories)
    {
        List<PackageVulnerability> result = [.. (advisories ?? [])
            .Where(a => a.Affects(package.Id, package.Version))
            .OrderByDescending(a => a.Severity)
            .ThenBy(a => a.AdvisoryUrl, StringComparer.Ordinal)
            .Select(a => new PackageVulnerability
            {
                AdvisoryUrl = a.AdvisoryUrl,
                Severity = a.Severity.ToString(CultureInfo.InvariantCulture),
            })];

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Whether an advisory affects a package version.
    /// </summary>
    /// <param name="advisory">The advisory.</param>
    /// <param name="packageId">The package's ID.</param>
    /// <param name="version">The package's version.</param>
    /// <returns>True if the version is in the advisory's range.</returns>
    public static bool Affects(this VulnerabilityAdvisory advisory, string packageId, NuGetVersion version)
    {
        if (!string.Equals(advisory.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            return false;

        return VersionRange.TryParse(advisory.VersionRange, out VersionRange? range) && range.Satisfies(version);
    }
}
