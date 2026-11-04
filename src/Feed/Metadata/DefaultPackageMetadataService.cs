using NuDepot.Core.Models;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Vulnerabilities;
using NuGet.Versioning;

namespace NuDepot.Feed.Metadata;

/// <inheritdoc />
public class DefaultPackageMetadataService(IPackageService packages, ICatalogService catalog, IVulnerabilityService vulnerabilities, RegistrationBuilder builder) : IPackageMetadataService
{
    private IPackageService Packages { get; } = packages;
    private ICatalogService Catalog { get; } = catalog;
    private IVulnerabilityService Vulnerabilities { get; } = vulnerabilities;
    private RegistrationBuilder Builder { get; } = builder;

    public async Task<NuDepotRegistrationIndexResponse?> GetRegistrationIndexOrNullAsync(string packageId, bool semVer2 = false, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Package> packages = await Packages.FindPackagesAsync(packageId, cancellationToken);

        // Only RegistrationsBaseUrl/3.6.0 may show SemVer 2.0.0 packages to clients.
        if (!semVer2)
            packages = [.. packages.Where(p => !p.IsSemVer2())];

        if (!packages.Any())
            return null;

        IReadOnlyDictionary<string, string> catalogLeafUrls = await Catalog.FindLatestLeafUrlsAsync(packageId, cancellationToken);
        IReadOnlyList<VulnerabilityAdvisory> vulnerabilities = await Vulnerabilities.FindAsync([packageId], cancellationToken);

        return Builder.BuildIndex(
            new PackageRegistration(
                packageId,
                packages,
                semVer2,
                catalogLeafUrls,
                vulnerabilities));
    }

    public async Task<RegistrationLeafResponse?> GetRegistrationLeafOrNullAsync(string id, NuGetVersion version, bool semVer2 = false, CancellationToken cancellationToken = default)
    {
        Package? package = await Packages.FindPackageOrNullAsync(id, version, cancellationToken);
        if (package == null || (!semVer2 && package.IsSemVer2()))
            return null;

        IReadOnlyDictionary<string, string> catalogLeafUrls = await Catalog.FindLatestLeafUrlsAsync(id, cancellationToken);
        catalogLeafUrls.TryGetValue(package.Version.ToFullString().ToLowerInvariant(), out string? catalogLeafUrl);

        return Builder.BuildLeaf(package, semVer2, catalogLeafUrl);
    }
}
