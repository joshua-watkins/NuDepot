using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Core.Models;
using NuGet.Feed.Catalog;
using NuGet.Feed.Vulnerabilities;
using NuGet.Versioning;

namespace NuGet.Feed.Metadata;

/// <inheritdoc />
public class DefaultPackageMetadataService : IPackageMetadataService
{
    private readonly IPackageService _packages;
    private readonly ICatalogService _catalog;
    private readonly IVulnerabilityService _vulnerabilities;
    private readonly RegistrationBuilder _builder;

    public DefaultPackageMetadataService(
        IPackageService packages,
        ICatalogService catalog,
        IVulnerabilityService vulnerabilities,
        RegistrationBuilder builder)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _vulnerabilities = vulnerabilities ?? throw new ArgumentNullException(nameof(vulnerabilities));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    public async Task<NuGetFeedRegistrationIndexResponse?> GetRegistrationIndexOrNullAsync(
        string packageId,
        bool semVer2 = false,
        CancellationToken cancellationToken = default)
    {
        var packages = await _packages.FindPackagesAsync(packageId, cancellationToken);

        // Only RegistrationsBaseUrl/3.6.0 may show SemVer 2.0.0 packages to clients.
        if (!semVer2)
        {
            packages = packages.Where(p => !p.IsSemVer2()).ToList();
        }

        if (!packages.Any())
        {
            return null;
        }

        var catalogLeafUrls = await _catalog.FindLatestLeafUrlsAsync(packageId, cancellationToken);
        var vulnerabilities = await _vulnerabilities.FindAsync(new[] { packageId }, cancellationToken);

        return _builder.BuildIndex(
            new PackageRegistration(
                packageId,
                packages,
                semVer2,
                catalogLeafUrls,
                vulnerabilities));
    }

    public async Task<RegistrationLeafResponse?> GetRegistrationLeafOrNullAsync(
        string id,
        NuGetVersion version,
        bool semVer2 = false,
        CancellationToken cancellationToken = default)
    {
        var package = await _packages.FindPackageOrNullAsync(id, version, cancellationToken);
        if (package == null || (!semVer2 && package.IsSemVer2()))
        {
            return null;
        }

        var catalogLeafUrls = await _catalog.FindLatestLeafUrlsAsync(id, cancellationToken);
        catalogLeafUrls.TryGetValue(package.Version.ToFullString().ToLowerInvariant(), out var catalogLeafUrl);

        return _builder.BuildLeaf(package, semVer2, catalogLeafUrl);
    }
}
