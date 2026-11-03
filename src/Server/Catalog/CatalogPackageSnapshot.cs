using System;
using System.Collections.Generic;
using System.Linq;
using NuGetFeed.Entities;
using NuGetFeed.Metadata;
using NuGetFeed.Models;

namespace NuGetFeed.Catalog;

/// <summary>
/// A package's state when it was added to the catalog. Stored as JSON in <see cref="CatalogEntry.Content"/>.
/// Embedded icons and licenses are stored as flags because their URLs depend on the server's address.
/// </summary>
public class CatalogPackageSnapshot
{
    public string? Authors { get; set; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public bool HasEmbeddedIcon { get; set; }
    public bool IsPrerelease { get; set; }
    public string? Language { get; set; }
    public string? LicenseUrl { get; set; }
    public bool HasEmbeddedLicense { get; set; }
    public bool LicenseIsMarkDown { get; set; }
    public string? LicenseExpression { get; set; }
    public bool Listed { get; set; }
    public string? MinClientVersion { get; set; }
    public string? PackageHash { get; set; }
    public long PackageSize { get; set; }
    public string? ProjectUrl { get; set; }
    public DateTime Published { get; set; }
    public string? ReleaseNotes { get; set; }
    public bool RequireLicenseAcceptance { get; set; }
    public string? Summary { get; set; }
    public List<string>? Tags { get; set; }
    public string? Title { get; set; }
    public string? VerbatimVersion { get; set; }
    public List<DependencyGroupItem>? DependencyGroups { get; set; }
    public List<CatalogPackageType>? PackageTypes { get; set; }
    public PackageDeprecation? Deprecation { get; set; }
    public List<PackageVulnerability>? Vulnerabilities { get; set; }

    public static CatalogPackageSnapshot Create(Package package, IEnumerable<VulnerabilityAdvisory> advisories)
    {
        return new CatalogPackageSnapshot
        {
            Authors = string.Join(", ", package.Authors ?? Array.Empty<string>()),
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
            DependencyGroups = package.ToDependencyGroups().ToList(),
            PackageTypes = package.PackageTypes
                .Where(t => t.Name != null)
                .Select(t => new CatalogPackageType
                {
                    Name = t.Name!,

                    // Package types without an explicit version are stored as version 0.0.
                    Version = string.IsNullOrEmpty(t.Version) || t.Version == "0.0" ? null : t.Version,
                })
                .ToList(),
            Deprecation = package.ToDeprecationOrNull(),
            Vulnerabilities = package.ToVulnerabilitiesOrNull(advisories),
        };
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
