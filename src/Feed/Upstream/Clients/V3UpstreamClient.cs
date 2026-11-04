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
public class V3UpstreamClient(NuGetClient client, ILogger<V3UpstreamClient> logger) : IUpstreamClient
{
    private NuGetClient Client { get; } = client;
    private ILogger<V3UpstreamClient> Logger { get; } = logger;

    /// <inheritdoc />
    public async Task<Stream?> DownloadPackageOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        try
        {
            using (Stream downloadStream = await Client.DownloadPackageAsync(id, version, cancellationToken))
            {
                return await downloadStream.AsTemporaryFileStreamAsync(cancellationToken);
            }
        }
        catch (PackageNotFoundException)
        {
            return null;
        }
        catch (Exception e)
        {
            Logger.LogError(
                e,
                "Failed to download {PackageId} {PackageVersion} from upstream",
                id,
                version);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Package>> ListPackagesAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<PackageMetadata> packages = await Client.GetPackageMetadataAsync(id, cancellationToken);

            return [.. packages.Select(ToPackage)];
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to mirror {PackageId}'s upstream metadata", id);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            return await Client.ListPackageVersionsAsync(id, includeUnlisted: true, cancellationToken);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to mirror {PackageId}'s upstream versions", id);
            return [];
        }
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

    private Uri? ParseUri(string? uriString)
    {
        if (uriString == null)
            return null;

        if (!Uri.TryCreate(uriString, UriKind.Absolute, out Uri? uri))
            return null;

        return uri;
    }

    private string[] ParseAuthors(string? authors)
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
}
