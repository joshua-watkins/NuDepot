using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NuDepot.Core.Models;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Serialization;
using NuDepot.Feed.Storage;
using NuGet.Versioning;
using System.Text.Json;

namespace NuDepot.Feed.Catalog;

/// <inheritdoc />
public class CatalogService(NuDepotContext context, IUrlGenerator url, IPackageStorageService storage, SystemTime time, ILogger<CatalogService> logger) : ICatalogService
{
    /// <summary>
    /// The most catalog items in a page, the same as nuget.org.
    /// </summary>
    public const int MaxPageSize = 550;

    /// <summary>
    /// The hash algorithm of <see cref="CatalogPackageSnapshot.PackageHash"/>.
    /// </summary>
    public const string PackageHashAlgorithm = "SHA512";

    private static readonly IReadOnlyList<string> PackageDetailsLeafType = [CatalogEntry.PackageDetailsType, "catalog:Permalink"];
    private static readonly IReadOnlyList<string> PackageDeleteLeafType = [CatalogEntry.PackageDeleteType, "catalog:Permalink"];

    // Commits must have strictly increasing timestamps. Serialize them within this process.
    private static readonly SemaphoreSlim CommitLock = new(1, 1);

    private NuDepotContext Context { get; } = context;
    private IUrlGenerator Url { get; } = url;
    private IPackageStorageService Storage { get; } = storage;
    private SystemTime Time { get; } = time;
    private ILogger<CatalogService> Logger { get; } = logger;

    /// <inheritdoc />
    public async Task AddPackageDetailsAsync(IReadOnlyList<Package> packages, CancellationToken cancellationToken)
    {
        if (packages == null || packages.Count == 0)
            return;

        List<string> ids = [.. packages.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase)];
        List<VulnerabilityAdvisory> advisories = await Context.VulnerabilityAdvisories
            .Where(a => ids.Contains(a.PackageId))
            .ToListAsync(cancellationToken);

        List<CatalogEntry> entries = [.. packages
            .Select(package => new CatalogEntry
            {
                Type = CatalogEntry.PackageDetailsType,
                PackageId = package.Id,
                PackageVersion = package.Version.ToFullString(),
                Content = JsonSerializer.Serialize(
                    CatalogPackageSnapshot.Create(package, advisories),
                    ServerJsonContext.Default.CatalogPackageSnapshot),
            })];

        await CommitAsync(entries, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddPackageDeleteAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        await CommitAsync(
            [
                new CatalogEntry
                {
                    Type = CatalogEntry.PackageDeleteType,
                    PackageId = id,
                    PackageVersion = version.ToFullString(),
                }
            ],
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CatalogIndex> GetIndexAsync(CancellationToken cancellationToken)
    {
        var commits = await Context.CatalogEntries
            .OrderBy(e => e.Key)
            .Select(e => new { e.CommitId, e.CommittedAt })
            .ToListAsync(cancellationToken);

        List<CatalogPageItem> pages = [.. commits
            .Chunk(MaxPageSize)
            .Select((page, index) => new CatalogPageItem
            {
                CatalogPageUrl = Url.GetCatalogPageUrl(index),
                CommitId = page[^1].CommitId,
                CommitTimestamp = ToUtc(page[^1].CommittedAt),
                Count = page.Length,
            })];

        var latest = commits.LastOrDefault();

        return new CatalogIndex
        {
            CommitId = latest?.CommitId ?? Guid.Empty.ToString(),
            CommitTimestamp = latest == null ? DateTimeOffset.MinValue : ToUtc(latest.CommittedAt),
            Count = pages.Count,
            Items = pages,
        };
    }

    /// <inheritdoc />
    public async Task<CatalogPage?> GetPageOrNullAsync(int page, CancellationToken cancellationToken)
    {
        if (page < 0)
            return null;

        var entries = await Context.CatalogEntries
            .OrderBy(e => e.Key)
            .Skip(page * MaxPageSize)
            .Take(MaxPageSize)
            .Select(e => new { e.Key, e.CommitId, e.CommittedAt, e.Type, e.PackageId, e.PackageVersion })
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
            return null;

        return new CatalogPage
        {
            CommitId = entries[^1].CommitId,
            CommitTimestamp = ToUtc(entries[^1].CommittedAt),
            Count = entries.Count,
            CatalogIndexUrl = Url.GetCatalogIndexUrl(),
            Items = [.. entries
                .Select(e => new CatalogLeafItem
                {
                    CatalogLeafUrl = Url.GetCatalogLeafUrl(e.Key, e.PackageId, e.PackageVersion),
                    Type = "nuget:" + e.Type,
                    CommitId = e.CommitId,
                    CommitTimestamp = ToUtc(e.CommittedAt),
                    PackageId = e.PackageId,
                    PackageVersion = e.PackageVersion,
                })],
        };
    }

    /// <inheritdoc />
    public async Task<CatalogLeaf?> GetLeafOrNullAsync(int key, CancellationToken cancellationToken)
    {
        CatalogEntry? entry = await Context.CatalogEntries.SingleOrDefaultAsync(e => e.Key == key, cancellationToken);
        if (entry == null)
            return null;

        string leafUrl = Url.GetCatalogLeafUrl(entry.Key, entry.PackageId, entry.PackageVersion);

        if (entry.Type == CatalogEntry.PackageDeleteType)
        {
            return new PackageDeleteCatalogLeaf
            {
                CatalogLeafUrl = leafUrl,
                Type = PackageDeleteLeafType,
                CommitId = entry.CommitId,
                CommitTimestamp = ToUtc(entry.CommittedAt),
                PackageId = entry.PackageId,
                PackageVersion = entry.PackageVersion,

                // For deletions, "published" is when the package was deleted.
                Published = ToUtc(entry.CommittedAt),
            };
        }

        CatalogPackageSnapshot snapshot = JsonSerializer.Deserialize(entry.Content ?? "{}", ServerJsonContext.Default.CatalogPackageSnapshot)
            ?? new CatalogPackageSnapshot();
        NuGetVersion version = NuGetVersion.Parse(entry.PackageVersion);

        return new PackageDetailsCatalogLeaf
        {
            CatalogLeafUrl = leafUrl,
            Type = PackageDetailsLeafType,
            CommitId = entry.CommitId,
            CommitTimestamp = ToUtc(entry.CommittedAt),
            PackageId = entry.PackageId,
            PackageVersion = entry.PackageVersion,
            Published = snapshot.Published,
            Created = snapshot.Published,
            LastEdited = ToUtc(entry.CommittedAt),
            Authors = snapshot.Authors,
            DependencyGroups = snapshot.DependencyGroups,
            Deprecation = snapshot.Deprecation,
            Description = snapshot.Description,
            IconUrl = snapshot.HasEmbeddedIcon
                ? Url.GetPackageIconDownloadUrl(entry.PackageId, version)
                : snapshot.IconUrl,
            IsPrerelease = snapshot.IsPrerelease,
            Language = snapshot.Language,
            LicenseUrl = snapshot.HasEmbeddedLicense
                ? Url.GetPackageLicenseDownloadUrl(entry.PackageId, version, snapshot.LicenseIsMarkDown)
                : snapshot.LicenseUrl,
            LicenseExpression = snapshot.LicenseExpression,
            Listed = snapshot.Listed,
            MinClientVersion = snapshot.MinClientVersion,
            PackageHash = snapshot.PackageHash,
            PackageHashAlgorithm = snapshot.PackageHash == null ? null : PackageHashAlgorithm,
            PackageSize = snapshot.PackageSize,
            PackageTypes = snapshot.PackageTypes,
            ProjectUrl = snapshot.ProjectUrl,
            ReleaseNotes = snapshot.ReleaseNotes,
            RequireLicenseAcceptance = snapshot.RequireLicenseAcceptance,
            Summary = snapshot.Summary,
            Tags = snapshot.Tags,
            Title = snapshot.Title,
            VerbatimVersion = snapshot.VerbatimVersion,
            Vulnerabilities = snapshot.Vulnerabilities,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> FindLatestLeafUrlsAsync(string id, CancellationToken cancellationToken)
    {
        var entries = await Context.CatalogEntries
            .Where(e => e.PackageId == id && e.Type == CatalogEntry.PackageDetailsType)
            .Select(e => new { e.Key, e.PackageId, e.PackageVersion })
            .ToListAsync(cancellationToken);

        return entries
            .GroupBy(e => e.PackageVersion.ToLowerInvariant())
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var latest = group.MaxBy(e => e.Key)!;
                    return Url.GetCatalogLeafUrl(latest.Key, latest.PackageId, latest.PackageVersion);
                });
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (await Context.CatalogEntries.AnyAsync(cancellationToken))
            return;

        List<Package> packages = await Context.Packages
            .Include(p => p.Dependencies)
            .Include(p => p.PackageTypes)
            .OrderBy(p => p.PublishedAt)
            .ToListAsync(cancellationToken);

        if (packages.Count == 0)
            return;

        Logger.LogInformation("Adding {PackageCount} existing packages to the catalog...", packages.Count);

        // Packages indexed before the catalog existed don't have the hash the catalog requires.
        foreach (Package? package in packages.Where(p => p.PackageHash == null))
        {
            try
            {
                using Stream packageStream = await Storage.GetPackageStreamAsync(package.Id, package.Version, cancellationToken)
                    ?? throw new FileNotFoundException("The package isn't in storage.");

                (package.PackageHash, package.PackageSize) = await packageStream.ComputeSha512Async(cancellationToken);

                string? hash = package.PackageHash;
                long size = package.PackageSize;
                await Context.Packages
                    .Where(p => p.Key == package.Key)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.PackageHash, hash).SetProperty(p => p.PackageSize, size), cancellationToken);
            }
            catch (Exception e)
            {
                Logger.LogWarning(
                    e,
                    "Could not hash package {PackageId} {PackageVersion}; its catalog entry won't have a hash",
                    package.Id,
                    package.NormalizedVersionString);
            }
        }

        await AddPackageDetailsAsync(packages, cancellationToken);

        Logger.LogInformation("Added {PackageCount} existing packages to the catalog", packages.Count);
    }

    private async Task CommitAsync(IReadOnlyList<CatalogEntry> entries, CancellationToken cancellationToken)
    {
        await CommitLock.WaitAsync(cancellationToken);
        try
        {
            DateTime? latest = await Context.CatalogEntries
                .OrderByDescending(e => e.Key)
                .Select(e => (DateTime?)e.CommittedAt)
                .FirstOrDefaultAsync(cancellationToken);

            DateTime timestamp = Time.UtcNow;
            if (latest.HasValue && timestamp <= latest.Value)
                timestamp = latest.Value.AddTicks(1);

            string commitId = Guid
                .NewGuid()
                .ToString();
            foreach (CatalogEntry entry in entries)
            {
                entry.CommitId = commitId;
                entry.CommittedAt = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
            }

            await Context.CatalogEntries.ExecuteInsertAsync(entries, cancellationToken);
        }
        finally
        {
            CommitLock.Release();
        }
    }

    private static DateTimeOffset ToUtc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
