namespace NuDepot.Feed.Entities;

/// <summary>
/// An item in the catalog: a snapshot of a package version when it was pushed, unlisted,
/// relisted, deprecated or had its vulnerabilities changed, or a record of its deletion.
/// Entries are only ever appended.
/// </summary>
public class CatalogEntry
{
    /// <summary>
    /// The type of an entry that snapshots a package version.
    /// </summary>
    public const string PackageDetailsType = "PackageDetails";

    /// <summary>
    /// The type of an entry that records a package version's deletion.
    /// </summary>
    public const string PackageDeleteType = "PackageDelete";

    /// <summary>
    /// Increases with every entry, which orders the catalog.
    /// </summary>
    public int Key { get; set; }

    /// <summary>
    /// The ID of the commit the entry was added in. Entries added together share it.
    /// </summary>
    public string CommitId { get; set; } = null!;

    /// <summary>
    /// When the entry was committed, in UTC.
    /// </summary>
    public DateTime CommittedAt { get; set; }

    /// <summary>
    /// <see cref="PackageDetailsType"/> or <see cref="PackageDeleteType"/>.
    /// </summary>
    public string Type { get; set; } = null!;

    /// <summary>
    /// The package's ID.
    /// </summary>
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
