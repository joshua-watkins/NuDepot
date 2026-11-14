namespace NuDepot.Feed.Authentication;

/// <summary>
/// A set of <see cref="FeedPermissions"/> given to an API key, or to requests without one.
/// </summary>
public enum FeedRole
{
    /// <summary>
    /// No access to the feed.
    /// </summary>
    None,

    /// <summary>
    /// Read and download packages.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// Read packages, push packages and symbols, and unlist or relist packages.
    /// </summary>
    ReadWrite,

    /// <summary>
    /// Everything, including permanently deleting packages, deprecating packages
    /// and managing vulnerabilities.
    /// </summary>
    Admin,
}
