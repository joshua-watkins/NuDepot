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

/// <summary>
/// Extensions to <see cref="FeedRole"/>.
/// </summary>
public static class FeedRoleExtensions
{
    /// <summary>
    /// Gets what a role may do.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>The role's permissions.</returns>
    public static FeedPermissions GetPermissions(this FeedRole role) => role switch
    {
        FeedRole.None => FeedPermissions.None,
        FeedRole.ReadOnly => FeedPermissions.Read,
        FeedRole.ReadWrite => FeedPermissions.Read | FeedPermissions.Push | FeedPermissions.Unlist,
        FeedRole.Admin => FeedPermissions.All,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown feed role."),
    };
}
