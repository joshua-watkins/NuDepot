using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Core;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;
using NuGet.Versioning;

namespace NuDepot.Feed.Upstream.Clients;

/// <summary>
/// The feed's mirrors, asked in the order of <see cref="MirroringOptions.Mirrors"/>. The first mirror that has a
/// package ID provides it, so a later mirror, like nuget.org after a private feed, can't add versions of a package
/// an earlier mirror has. If a mirror can't be asked, the later mirrors aren't either, so a package isn't taken from
/// another feed while its own feed is down. Without mirroring, nothing is found.
/// </summary>
internal sealed class MirrorChain : IUpstreamClient
{
    public MirrorChain(IOptions<MirroringOptions> options, HttpClient httpClient, ILoggerFactory loggerFactory)
    {
        ILogger<V3UpstreamClient> logger = loggerFactory.CreateLogger<V3UpstreamClient>();
        MirroringOptions mirroring = options.Value;

        Mirrors = !mirroring.IsEnabled ? [] : [.. mirroring.Mirrors
            .Select(mirror => new V3UpstreamClient(
                new NuGetClient(new NuGetClientFactory(httpClient, mirror.PackageSource!.ToString())),
                logger,
                mirror.DisplayName,
                TimeSpan.FromSeconds(mirror.PackageDownloadTimeoutSeconds)))];
    }

    private IReadOnlyList<V3UpstreamClient> Mirrors { get; }

    /// <summary>
    /// Whether mirroring is on.
    /// </summary>
    public bool HasMirrors => Mirrors.Count > 0;

    /// <inheritdoc />
    public async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string id, CancellationToken cancellationToken)
    {
        foreach (V3UpstreamClient mirror in Mirrors)
        {
            IReadOnlyList<NuGetVersion>? versions = await mirror.TryListPackageVersionsAsync(id, cancellationToken);
            if (versions == null || versions.Count > 0)
                return versions ?? [];
        }

        return [];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Package>> ListPackagesAsync(string id, CancellationToken cancellationToken)
    {
        foreach (V3UpstreamClient mirror in Mirrors)
        {
            IReadOnlyList<Package>? packages = await mirror.TryListPackagesAsync(id, cancellationToken);
            if (packages == null || packages.Count > 0)
                return packages ?? [];
        }

        return [];
    }

    /// <inheritdoc />
    public async Task<Stream?> DownloadPackageOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        for (int i = 0; i < Mirrors.Count; i++)
        {
            V3UpstreamClient mirror = Mirrors[i];

            // The last mirror is the only one left that could have the package, so it isn't asked first.
            if (i == Mirrors.Count - 1)
                return await mirror.DownloadPackageOrNullAsync(id, version, cancellationToken);

            IReadOnlyList<NuGetVersion>? versions = await mirror.TryListPackageVersionsAsync(id, cancellationToken);
            if (versions == null)
                return null;
            if (versions.Count > 0)
                return await mirror.DownloadPackageOrNullAsync(id, version, cancellationToken);
        }

        return null;
    }
}
