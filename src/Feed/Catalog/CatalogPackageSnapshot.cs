using NuDepot.Core.Models;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Metadata;

namespace NuDepot.Feed.Catalog;

/// <summary>
/// A package's state when it was added to the catalog. Stored as JSON in <see cref="CatalogEntry.Content"/>.
/// Embedded icons and licenses are stored as flags because their URLs depend on the server's address.
/// </summary>
public class CatalogPackageSnapshot
{
    /// <summary>
    /// The package's authors, separated by commas.
    /// </summary>
    public string? Authors { get; set; }

    /// <summary>
    /// The package's description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The URL of the package's icon, if it links to one.
    /// </summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// Whether the package has an icon file.
    /// </summary>
    public bool HasEmbeddedIcon { get; set; }

    /// <summary>
    /// Whether the version is a prerelease.
    /// </summary>
    public bool IsPrerelease { get; set; }

    /// <summary>
    /// The package's language, like "en-US".
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// The URL of the package's license, if it links to one.
    /// </summary>
    public string? LicenseUrl { get; set; }

    /// <summary>
    /// Whether the package has a license file.
    /// </summary>
    public bool HasEmbeddedLicense { get; set; }

    /// <summary>
    /// Whether the license file is Markdown, rather than text.
    /// </summary>
    public bool LicenseIsMarkDown { get; set; }

    /// <summary>
    /// The package's SPDX license expression, if it uses one.
    /// </summary>
    public string? LicenseExpression { get; set; }

    /// <summary>
    /// Whether the version was listed.
    /// </summary>
    public bool Listed { get; set; }

    /// <summary>
    /// The oldest NuGet client that can install the package.
    /// </summary>
    public string? MinClientVersion { get; set; }

    /// <summary>
    /// The base64-encoded SHA-512 hash of the package.
    /// </summary>
    public string? PackageHash { get; set; }

    /// <summary>
    /// The size of the package in bytes.
    /// </summary>
    public long PackageSize { get; set; }

    /// <summary>
    /// The URL of the package's project.
    /// </summary>
    public string? ProjectUrl { get; set; }

    /// <summary>
    /// When the version was published.
    /// </summary>
    public DateTime Published { get; set; }

    /// <summary>
    /// The package's release notes.
    /// </summary>
    public string? ReleaseNotes { get; set; }

    /// <summary>
    /// Whether users must accept the license to install the package.
    /// </summary>
    public bool RequireLicenseAcceptance { get; set; }

    /// <summary>
    /// The package's summary.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// The package's tags.
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// The package's title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The version as the package wrote it.
    /// </summary>
    public string? VerbatimVersion { get; set; }

    /// <summary>
    /// The package's dependencies, grouped by target framework.
    /// </summary>
    public List<DependencyGroupItem>? DependencyGroups { get; set; }

    /// <summary>
    /// The package's types, like "Dependency" or "DotnetTool".
    /// </summary>
    public List<CatalogPackageType>? PackageTypes { get; set; }

    /// <summary>
    /// The version's deprecation, if it was deprecated.
    /// </summary>
    public PackageDeprecation? Deprecation { get; set; }

    /// <summary>
    /// The known vulnerabilities that affected the version.
    /// </summary>
    public List<PackageVulnerability>? Vulnerabilities { get; set; }

    /// <summary>
    /// Takes a snapshot of a package's current state.
    /// </summary>
    /// <param name="package">The package, with its dependencies and package types loaded.</param>
    /// <param name="advisories">The package ID's known vulnerabilities. Those that affect the version are kept.</param>
    /// <returns>The snapshot.</returns>
    public static CatalogPackageSnapshot Create(Package package, IEnumerable<VulnerabilityAdvisory> advisories)
    {
        return new CatalogPackageSnapshot
        {
            Authors = string.Join(", ", package.Authors ?? []),
            Description = package.Description,
            IconUrl = NullIfEmpty(package.IconUrlString),
            HasEmbeddedIcon = package.HasEmbeddedIcon,
            IsPrerelease = package.IsPrerelease,
            Language = NullIfEmpty(package.Language),
            LicenseUrl = NullIfEmpty(package.LicenseUrlString),
            HasEmbeddedLicense = package.HasEmbeddedLicense,
            LicenseIsMarkDown = package.LicenseIsMarkDown,
            LicenseExpression = package.LicenseExpression,
            Listed = package.Listed,
            MinClientVersion = NullIfEmpty(package.MinClientVersion),
            PackageHash = package.PackageHash,
            PackageSize = package.PackageSize,
            ProjectUrl = NullIfEmpty(package.ProjectUrlString),
            Published = DateTime.SpecifyKind(package.PublishedAt, DateTimeKind.Utc),
            ReleaseNotes = NullIfEmpty(package.ReleaseNotes),
            RequireLicenseAcceptance = package.RequireLicenseAcceptance,
            Summary = NullIfEmpty(package.Summary),
            Tags = package.Tags?.ToList(),
            Title = NullIfEmpty(package.Title),
            VerbatimVersion = package.OriginalVersionString,
            DependencyGroups = [.. package.ToDependencyGroups()],
            PackageTypes = [.. package.PackageTypes
                .Where(t => t.Name != null)
                .Select(t => new CatalogPackageType
                {
                    Name = t.Name!,

                    // Package types without an explicit version are stored as version 0.0.
                    Version = string.IsNullOrEmpty(t.Version) || t.Version == "0.0" ? null : t.Version,
                })],
            Deprecation = package.ToDeprecationOrNull(),
            Vulnerabilities = package.ToVulnerabilitiesOrNull(advisories),
        };
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
