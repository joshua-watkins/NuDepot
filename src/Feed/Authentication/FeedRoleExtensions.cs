namespace NuDepot.Feed.Authentication;

/// <summary>
/// Extensions to <see cref="FeedRole"/>.
/// </summary>
internal static class FeedRoleExtensions
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
