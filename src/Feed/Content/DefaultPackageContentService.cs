using NuDepot.Core.Models;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Signing;
using NuDepot.Feed.Storage;
using NuGet.Versioning;

namespace NuDepot.Feed.Content;

/// <summary>
/// Implements the NuGet Package Content resource in NuGet's V3 protocol.
/// </summary>
/// <param name="packages">Finds packages.</param>
/// <param name="storage">Where packages are stored.</param>
/// <param name="resigner">Re-signs packages signed with an older certificate as they're downloaded.</param>
internal class DefaultPackageContentService(IPackageService packages, IPackageStorageService storage, PackageResigner resigner) : IPackageContentService
{
    private IPackageService Packages { get; } = packages;
    private IPackageStorageService Storage { get; } = storage;
    private PackageResigner Resigner { get; } = resigner;

    /// <inheritdoc />
    public async Task<PackageVersionsResponse?> GetPackageVersionsOrNullAsync(string id, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NuGetVersion> versions = await Packages.FindPackageVersionsAsync(id, cancellationToken);
        if (versions.Count == 0)
            return null;

        return new PackageVersionsResponse
        {
            Versions = [.. versions
                .Select(v => v.ToNormalizedString())
                .Select(v => v.ToLowerInvariant())]
        };
    }

    /// <inheritdoc />
    public async Task<Stream?> GetPackageContentStreamOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
    {
        if (!await Packages.ExistsAsync(id, version, cancellationToken))
            return null;

        await Packages.AddDownloadAsync(id, version, cancellationToken);
        Stream? content = await Storage.GetPackageStreamAsync(id, version, cancellationToken);

        return content == null ? null : await Resigner.RefreshAsync(id, version, content, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream?> GetPackageManifestStreamOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
    {
        if (!await Packages.ExistsAsync(id, version, cancellationToken))
            return null;

        return await Storage.GetNuspecStreamAsync(id, version, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream?> GetPackageReadmeStreamOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
    {
        Package? package = await Packages.FindPackageOrNullAsync(id, version, cancellationToken);
        if (package == null || !package.HasReadme)
            return null;

        return await Storage.GetReadmeStreamAsync(id, version, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream?> GetPackageIconStreamOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
    {
        Package? package = await Packages.FindPackageOrNullAsync(id, version, cancellationToken);
        if (package == null || !package.HasEmbeddedIcon)
            return null;

        return await Storage.GetIconStreamAsync(id, version, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream?> GetPackageLicenseStreamOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken = default)
    {
        Package? package = await Packages.FindPackageOrNullAsync(id, version, cancellationToken);
        if (package == null || !package.HasEmbeddedLicense)
            return null;

        return await Storage.GetLicenseStreamAsync(id, version, package.LicenseIsMarkDown, cancellationToken);
    }

}
