using NuDepot.Feed.Entities;
using NuGet.Versioning;

namespace NuDepot.Feed.Upstream.Clients;

/// <summary>
/// The client used when there are no upstream package sources.
/// </summary>
public class DisabledUpstreamClient : IUpstreamClient
{
    private IReadOnlyList<NuGetVersion> EmptyVersionList { get; } = [];
    private IReadOnlyList<Package> EmptyPackageList { get; } = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string id, CancellationToken cancellationToken)
    {
        return Task.FromResult(EmptyVersionList);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Package>> ListPackagesAsync(string id, CancellationToken cancellationToken)
    {
        return Task.FromResult(EmptyPackageList);
    }

    /// <inheritdoc />
    public Task<Stream?> DownloadPackageOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return Task.FromResult<Stream?>(null);
    }
}
