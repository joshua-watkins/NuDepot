namespace NuDepot.Feed.Users;

/// <summary>
/// The outcome of changing or deleting a user.
/// </summary>
internal enum UserChangeResult
{
    /// <summary>
    /// The user was changed or deleted.
    /// </summary>
    Changed,

    /// <summary>
    /// There's no such user.
    /// </summary>
    NotFound,

    /// <summary>
    /// The user is the feed's last Admin, who can't be deleted or given another role.
    /// </summary>
    LastAdmin,
}
