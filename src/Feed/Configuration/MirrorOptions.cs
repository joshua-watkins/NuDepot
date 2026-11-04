namespace NuDepot.Feed.Configuration;

/// <summary>
/// Read-through caching: packages that aren't on the feed are indexed from an upstream feed.
/// </summary>
public class MirrorOptions
{
    /// <summary>
    /// If true, packages that aren't found locally will be indexed
    /// using the upstream source.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The v3 index that will be mirrored. Required if <see cref="Enabled"/> is true.
    /// </summary>
    public Uri? PackageSource { get; set; }

    /// <summary>
    /// The time before a download from the package source times out. Can't be negative.
    /// </summary>
    public int PackageDownloadTimeoutSeconds { get; set; } = 600;
}
