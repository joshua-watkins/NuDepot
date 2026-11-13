using Microsoft.Extensions.Logging;
using NuDepot.Core;
using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuGet.Versioning;

namespace NuDepot.Feed.Upstream.Clients;

/// <summary>
/// The mirroring client for a NuGet server that uses the V3 protocol.
/// </summary>
/// <param name="client">The upstream feed's client.</param>
/// <param name="logger">The logger.</param>
/// <param name="name">The mirror's name, for the logs.</param>
/// <param name="requestTimeout">The time a request may take to answer, or null for no limit. A download's answer is
/// its headers.</param>
/// <param name="downloadTimeout">The time a package's content may take to download, after its headers, or null
/// for no limit.</param>
public partial class V3UpstreamClient(NuGetClient client, ILogger<V3UpstreamClient> logger, string name = "upstream", TimeSpan? requestTimeout = null, TimeSpan? downloadTimeout = null) : IUpstreamClient
{
    private NuGetClient Client { get; } = client;
    private ILogger<V3UpstreamClient> Logger { get; } = logger;
    private string Name { get; } = name;
    private TimeSpan? RequestTimeout { get; } = requestTimeout;
    private TimeSpan? DownloadTimeout { get; } = downloadTimeout;

    /// <inheritdoc />
    public async Task<Stream?> DownloadPackageOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
        => (await TryDownloadPackageAsync(id, version, cancellationToken)).Package;

    /// <summary>
    /// Like <see cref="DownloadPackageOrNullAsync"/>, but says whether the upstream feed couldn't be asked, rather
    /// than not having the package.
    /// </summary>
    internal async Task<(Stream? Package, bool Failed)> TryDownloadPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        try
        {
            // The client returns once the headers arrive, so the request timeout ends there, and the download
            // timeout covers the content.
            Stream downloadStream;
            using (CancellationTokenSource? requestTimeout = StartTimeout(RequestTimeout, cancellationToken))
            {
                downloadStream = await Client.DownloadPackageAsync(id, version, requestTimeout?.Token ?? cancellationToken);
            }

            using (downloadStream)
            using (CancellationTokenSource? downloadTimeout = StartTimeout(DownloadTimeout, cancellationToken))
            {
                return (await downloadStream.AsTemporaryFileStreamAsync(downloadTimeout?.Token ?? cancellationToken), false);
            }
        }
        catch (PackageNotFoundException)
        {
            return (null, false);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            Log.DownloadFailed(Logger, e, id, version, Name);
            return (null, true);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Package>> ListPackagesAsync(string id, CancellationToken cancellationToken)
        => await TryListPackagesAsync(id, cancellationToken) ?? [];

    /// <inheritdoc />
    public async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string id, CancellationToken cancellationToken)
        => await TryListPackageVersionsAsync(id, cancellationToken) ?? [];

    /// <summary>
    /// Like <see cref="ListPackagesAsync"/>, but null if the upstream feed couldn't be asked, rather than empty,
    /// which means it doesn't have the package.
    /// </summary>
    internal async Task<IReadOnlyList<Package>?> TryListPackagesAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource? timeout = StartTimeout(RequestTimeout, cancellationToken);
            IReadOnlyList<PackageMetadata> packages = await Client.GetPackageMetadataAsync(id, timeout?.Token ?? cancellationToken);

            return [.. packages.Select(ToPackage)];
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            Log.MetadataFailed(Logger, e, id, Name);
            return null;
        }
    }

    /// <summary>
    /// Like <see cref="ListPackageVersionsAsync"/>, but null if the upstream feed couldn't be asked, rather than
    /// empty, which means it doesn't have the package.
    /// </summary>
    internal async Task<IReadOnlyList<NuGetVersion>?> TryListPackageVersionsAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource? timeout = StartTimeout(RequestTimeout, cancellationToken);
            return await Client.ListPackageVersionsAsync(id, includeUnlisted: true, timeout?.Token ?? cancellationToken);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            Log.VersionsFailed(Logger, e, id, Name);
            return null;
        }
    }

    // Null without a timeout, so requests get the caller's token.
    private static CancellationTokenSource? StartTimeout(TimeSpan? limit, CancellationToken cancellationToken)
    {
        if (limit is not TimeSpan time)
            return null;

        CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(time);
        return timeout;
    }

    private Package ToPackage(Core.Models.PackageMetadata metadata)
    {
        NuGetVersion version = metadata.ParseVersion();

        return new Package
        {
            Id = metadata.PackageId,
            Version = version,
            Authors = ParseAuthors(metadata.Authors),
            Description = metadata.Description,
            Downloads = 0,
            HasReadme = false,
            IsPrerelease = version.IsPrerelease,
            Language = metadata.Language,
            Listed = metadata.IsListed(),
            MinClientVersion = metadata.MinClientVersion,
            PublishedAt = metadata.Published.UtcDateTime,
            RequireLicenseAcceptance = metadata.RequireLicenseAcceptance,
            Summary = metadata.Summary,
            Title = metadata.Title,
            IconUrl = ParseUri(metadata.IconUrl),
            LicenseUrl = ParseUri(metadata.LicenseUrl),
            ProjectUrl = ParseUri(metadata.ProjectUrl),
            PackageTypes = [],
            RepositoryUrl = null,
            RepositoryType = null,
            SemVerLevel = version.IsSemVer2 ? SemVerLevel.SemVer2 : SemVerLevel.Unknown,
            Tags = metadata.Tags?.ToArray() ?? [],

            Dependencies = ToDependencies(metadata)
        };
    }

    private static Uri? ParseUri(string? uriString)
    {
        if (uriString == null)
            return null;

        if (!Uri.TryCreate(uriString, UriKind.Absolute, out Uri? uri))
            return null;

        return uri;
    }

    private static string[] ParseAuthors(string? authors)
    {
        if (string.IsNullOrEmpty(authors))
            return [];

        return [.. authors
            .Split([',', ';', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim())];
    }

    private List<PackageDependency> ToDependencies(Core.Models.PackageMetadata package)
    {
        if (package.DependencyGroups == null || package.DependencyGroups.Count == 0)
            return [];

        return [.. package.DependencyGroups.SelectMany(ToDependencies)];
    }

    private IEnumerable<PackageDependency> ToDependencies(DependencyGroupItem group)
    {
        // NuDepot stores a dependency group with no dependencies as a package dependency
        // with no package id nor package version.
        if (group.Dependencies == null || group.Dependencies.Count == 0)
        {
            return
            [
                new PackageDependency
                {
                    Id = null,
                    VersionRange = null,
                    TargetFramework = group.TargetFramework,
                }
            ];
        }

        return group.Dependencies.Select(d => new PackageDependency
        {
            Id = d.Id,
            VersionRange = d.Range,
            TargetFramework = group.TargetFramework,
        });
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to download {PackageId} {PackageVersion} from mirror {Mirror}")]
        public static partial void DownloadFailed(ILogger logger, Exception exception, string packageId, NuGetVersion packageVersion, string mirror);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get {PackageId}'s metadata from mirror {Mirror}")]
        public static partial void MetadataFailed(ILogger logger, Exception exception, string packageId, string mirror);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get {PackageId}'s versions from mirror {Mirror}")]
        public static partial void VersionsFailed(ILogger logger, Exception exception, string packageId, string mirror);
    }
}
