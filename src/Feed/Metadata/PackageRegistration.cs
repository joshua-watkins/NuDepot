using NuDepot.Feed.Entities;

namespace NuDepot.Feed.Metadata;

/// <summary>
/// The information on all versions of a package.
/// </summary>
/// <param name="packageId"></param>
/// <param name="packages">All versions of the package.</param>
/// <param name="semVer2">Whether the registration is for the hive that includes SemVer 2.0.0 packages.</param>
/// <param name="catalogLeafUrls">The latest catalog leaf of each version, keyed by lowercase full normalized version.</param>
/// <param name="vulnerabilities">The package's known vulnerabilities.</param>
public class PackageRegistration(string packageId, IReadOnlyList<Package> packages, bool semVer2 = false, IReadOnlyDictionary<string, string>? catalogLeafUrls = null, IReadOnlyList<VulnerabilityAdvisory>? vulnerabilities = null)
{
    private static readonly IReadOnlyDictionary<string, string> NoCatalogLeafUrls = new Dictionary<string, string>();

    /// <summary>
    /// The package's ID.
    /// </summary>
    public string PackageId { get; } = packageId;

    /// <summary>
    /// The information for each version of the package.
    /// </summary>
    public IReadOnlyList<Package> Packages { get; } = packages;

    /// <summary>
    /// Whether this is the registration hive that includes SemVer 2.0.0 packages.
    /// </summary>
    public bool SemVer2 { get; } = semVer2;

    /// <summary>
    /// The latest catalog leaf URL of each version, keyed by lowercase full normalized version.
    /// </summary>
    public IReadOnlyDictionary<string, string> CatalogLeafUrls { get; } = catalogLeafUrls ?? NoCatalogLeafUrls;

    public IReadOnlyList<VulnerabilityAdvisory> Vulnerabilities { get; } = vulnerabilities ?? [];

    public string? GetCatalogLeafUrlOrNull(Package package)
        => CatalogLeafUrls.TryGetValue(package.Version.ToFullString().ToLowerInvariant(), out string? url) ? url : null;
}
