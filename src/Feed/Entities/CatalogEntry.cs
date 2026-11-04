using System;

namespace NuDepot.Feed.Entities;

/// <summary>
/// An item in the catalog: a snapshot of a package version when it was pushed, unlisted,
/// relisted, deprecated or had its vulnerabilities changed, or a record of its deletion.
/// Entries are only ever appended.
/// </summary>
public class CatalogEntry
{
    public const string PackageDetailsType = "PackageDetails";
    public const string PackageDeleteType = "PackageDelete";

    /// <summary>
    /// Increases with every entry, which orders the catalog.
    /// </summary>
    public int Key { get; set; }

    public string CommitId { get; set; } = null!;

    public DateTime CommittedAt { get; set; }

    /// <summary>
    /// <see cref="PackageDetailsType"/> or <see cref="PackageDeleteType"/>.
    /// </summary>
    public string Type { get; set; } = null!;

    public string PackageId { get; set; } = null!;

    /// <summary>
    /// The package's full normalized version, including SemVer 2.0.0 build metadata.
    /// </summary>
    public string PackageVersion { get; set; } = null!;

    /// <summary>
    /// The package's state at the time of the entry, as JSON. Null for deletions.
    /// URLs to this server are stored without a host, so they're rendered per request.
    /// </summary>
    public string? Content { get; set; }
}
