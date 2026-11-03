using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using NuGetFeed.Entities;
using NuGetFeed.Extensions;
using NuGetFeed.Models;
using NuGetFeed.Serialization;
using NuGetFeed.Storage;

namespace NuGetFeed.Catalog;

/// <inheritdoc />
public class CatalogService : ICatalogService
{
    /// <summary>
    /// The most catalog items in a page, the same as nuget.org.
    /// </summary>
    public const int MaxPageSize = 550;

    public const string PackageHashAlgorithm = "SHA512";

    private static readonly IReadOnlyList<string> PackageDetailsLeafType = new[] { CatalogEntry.PackageDetailsType, "catalog:Permalink" };
    private static readonly IReadOnlyList<string> PackageDeleteLeafType = new[] { CatalogEntry.PackageDeleteType, "catalog:Permalink" };

    // Commits must have strictly increasing timestamps. Serialize them within this process.
    private static readonly SemaphoreSlim CommitLock = new(1, 1);

    private readonly NuGetFeedContext _context;
    private readonly IUrlGenerator _url;
    private readonly IPackageStorageService _storage;
    private readonly SystemTime _time;
    private readonly ILogger<CatalogService> _logger;

    public CatalogService(
        NuGetFeedContext context,
        IUrlGenerator url,
        IPackageStorageService storage,
        SystemTime time,
        ILogger<CatalogService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task AddPackageDetailsAsync(IReadOnlyList<Package> packages, CancellationToken cancellationToken)
    {
        if (packages == null || packages.Count == 0) return;

        var ids = packages.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var advisories = await _context.VulnerabilityAdvisories
            .Where(a => ids.Contains(a.PackageId))
            .ToListAsync(cancellationToken);

        var entries = packages
            .Select(package => new CatalogEntry
            {
                Type = CatalogEntry.PackageDetailsType,
                PackageId = package.Id,
                PackageVersion = package.Version.ToFullString(),
                Content = JsonSerializer.Serialize(
                    CatalogPackageSnapshot.Create(package, advisories),
                    ServerJsonContext.Default.CatalogPackageSnapshot),
            })
            .ToList();

        await CommitAsync(entries, cancellationToken);
    }

    public async Task AddPackageDeleteAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        await CommitAsync(
            new[]
            {
                new CatalogEntry
                {
                    Type = CatalogEntry.PackageDeleteType,
                    PackageId = id,
                    PackageVersion = version.ToFullString(),
                }
            },
            cancellationToken);
    }

    public async Task<CatalogIndex> GetIndexAsync(CancellationToken cancellationToken)
    {
        var commits = await _context.CatalogEntries
            .OrderBy(e => e.Key)
            .Select(e => new { e.CommitId, e.CommittedAt })
            .ToListAsync(cancellationToken);

        var pages = commits
            .Chunk(MaxPageSize)
            .Select((page, index) => new CatalogPageItem
            {
                CatalogPageUrl = _url.GetCatalogPageUrl(index),
                CommitId = page[^1].CommitId,
                CommitTimestamp = ToUtc(page[^1].CommittedAt),
                Count = page.Length,
            })
            .ToList();

        var latest = commits.LastOrDefault();

        return new CatalogIndex
        {
            CommitId = latest?.CommitId ?? Guid.Empty.ToString(),
            CommitTimestamp = latest == null ? DateTimeOffset.MinValue : ToUtc(latest.CommittedAt),
            Count = pages.Count,
            Items = pages,
        };
    }

    public async Task<CatalogPage?> GetPageOrNullAsync(int page, CancellationToken cancellationToken)
    {
        if (page < 0) return null;

        var entries = await _context.CatalogEntries
            .OrderBy(e => e.Key)
            .Skip(page * MaxPageSize)
            .Take(MaxPageSize)
            .Select(e => new { e.Key, e.CommitId, e.CommittedAt, e.Type, e.PackageId, e.PackageVersion })
            .ToListAsync(cancellationToken);

        if (entries.Count == 0) return null;

        return new CatalogPage
        {
            CommitId = entries[^1].CommitId,
            CommitTimestamp = ToUtc(entries[^1].CommittedAt),
            Count = entries.Count,
            CatalogIndexUrl = _url.GetCatalogIndexUrl(),
            Items = entries
                .Select(e => new CatalogLeafItem
                {
                    CatalogLeafUrl = _url.GetCatalogLeafUrl(e.Key, e.PackageId, e.PackageVersion),
                    Type = "nuget:" + e.Type,
                    CommitId = e.CommitId,
                    CommitTimestamp = ToUtc(e.CommittedAt),
                    PackageId = e.PackageId,
                    PackageVersion = e.PackageVersion,
                })
                .ToList(),
        };
    }

    public async Task<CatalogLeaf?> GetLeafOrNullAsync(int key, CancellationToken cancellationToken)
    {
        var entry = await _context.CatalogEntries.SingleOrDefaultAsync(e => e.Key == key, cancellationToken);
        if (entry == null) return null;

        var leafUrl = _url.GetCatalogLeafUrl(entry.Key, entry.PackageId, entry.PackageVersion);

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

        var snapshot = JsonSerializer.Deserialize(entry.Content ?? "{}", ServerJsonContext.Default.CatalogPackageSnapshot)
            ?? new CatalogPackageSnapshot();
        var version = NuGetVersion.Parse(entry.PackageVersion);

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
                ? _url.GetPackageIconDownloadUrl(entry.PackageId, version)
                : snapshot.IconUrl,
            IsPrerelease = snapshot.IsPrerelease,
            Language = snapshot.Language,
            LicenseUrl = snapshot.HasEmbeddedLicense
                ? _url.GetPackageLicenseDownloadUrl(entry.PackageId, version, snapshot.LicenseIsMarkDown)
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

    public async Task<IReadOnlyDictionary<string, string>> FindLatestLeafUrlsAsync(string id, CancellationToken cancellationToken)
    {
        var entries = await _context.CatalogEntries
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
                    return _url.GetCatalogLeafUrl(latest.Key, latest.PackageId, latest.PackageVersion);
                });
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (await _context.CatalogEntries.AnyAsync(cancellationToken)) return;

        var packages = await _context.Packages
            .Include(p => p.Dependencies)
            .Include(p => p.PackageTypes)
            .OrderBy(p => p.PublishedAt)
            .ToListAsync(cancellationToken);

        if (packages.Count == 0) return;

        _logger.LogInformation("Adding {PackageCount} existing packages to the catalog...", packages.Count);

        // Packages indexed before the catalog existed don't have the hash the catalog requires.
        foreach (var package in packages.Where(p => p.PackageHash == null))
        {
            try
            {
                using var packageStream = await _storage.GetPackageStreamAsync(package.Id, package.Version, cancellationToken)
                    ?? throw new FileNotFoundException("The package isn't in storage.");

                (package.PackageHash, package.PackageSize) = await packageStream.ComputeSha512Async(cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogWarning(
                    e,
                    "Could not hash package {PackageId} {PackageVersion}; its catalog entry won't have a hash",
                    package.Id,
                    package.NormalizedVersionString);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        await AddPackageDetailsAsync(packages, cancellationToken);

        _logger.LogInformation("Added {PackageCount} existing packages to the catalog", packages.Count);
    }

    private async Task CommitAsync(IReadOnlyList<CatalogEntry> entries, CancellationToken cancellationToken)
    {
        await CommitLock.WaitAsync(cancellationToken);
        try
        {
            var latest = await _context.CatalogEntries
                .OrderByDescending(e => e.Key)
                .Select(e => (DateTime?)e.CommittedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var timestamp = _time.UtcNow;
            if (latest.HasValue && timestamp <= latest.Value)
            {
                timestamp = latest.Value.AddTicks(1);
            }

            var commitId = Guid.NewGuid().ToString();
            foreach (var entry in entries)
            {
                entry.CommitId = commitId;
                entry.CommittedAt = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
                _context.CatalogEntries.Add(entry);
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            CommitLock.Release();
        }
    }

    private static DateTimeOffset ToUtc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
