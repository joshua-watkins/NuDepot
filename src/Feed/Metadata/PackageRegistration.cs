using System;
using System.Collections.Generic;
using NuGet.Feed.Entities;

namespace NuGet.Feed.Metadata;

/// <summary>
/// The information on all versions of a package.
/// </summary>
public class PackageRegistration
{
    private static readonly IReadOnlyDictionary<string, string> NoCatalogLeafUrls = new Dictionary<string, string>();

    /// <summary>
    /// Create a new registration object.
    /// </summary>
    /// <param name="packageId"></param>
    /// <param name="packages">All versions of the package.</param>
    /// <param name="semVer2">Whether the registration is for the hive that includes SemVer 2.0.0 packages.</param>
    /// <param name="catalogLeafUrls">The latest catalog leaf of each version, keyed by lowercase full normalized version.</param>
    /// <param name="vulnerabilities">The package's known vulnerabilities.</param>
    public PackageRegistration(
        string packageId,
        IReadOnlyList<Package> packages,
        bool semVer2 = false,
        IReadOnlyDictionary<string, string>? catalogLeafUrls = null,
        IReadOnlyList<VulnerabilityAdvisory>? vulnerabilities = null)
    {
        PackageId = packageId ?? throw new ArgumentNullException(nameof(packageId));
        Packages = packages ?? throw new ArgumentNullException(nameof(packages));
        SemVer2 = semVer2;
        CatalogLeafUrls = catalogLeafUrls ?? NoCatalogLeafUrls;
        Vulnerabilities = vulnerabilities ?? Array.Empty<VulnerabilityAdvisory>();
    }

    /// <summary>
    /// The package's ID.
    /// </summary>
    public string PackageId { get; }

    /// <summary>
    /// The information for each version of the package.
    /// </summary>
    public IReadOnlyList<Package> Packages { get; }

    /// <summary>
    /// Whether this is the registration hive that includes SemVer 2.0.0 packages.
    /// </summary>
    public bool SemVer2 { get; }

    /// <summary>
    /// The latest catalog leaf URL of each version, keyed by lowercase full normalized version.
    /// </summary>
    public IReadOnlyDictionary<string, string> CatalogLeafUrls { get; }

    public IReadOnlyList<VulnerabilityAdvisory> Vulnerabilities { get; }

    public string? GetCatalogLeafUrlOrNull(Package package)
        => CatalogLeafUrls.TryGetValue(package.Version.ToFullString().ToLowerInvariant(), out var url) ? url : null;
}
