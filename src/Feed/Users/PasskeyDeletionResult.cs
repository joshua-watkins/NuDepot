namespace NuDepot.Feed.Users;

/// <summary>
/// The outcome of deleting a passkey.
/// </summary>
internal enum PasskeyDeletionResult
{
    /// <summary>
    /// The passkey was deleted.
    /// </summary>
    Deleted,

    /// <summary>
    /// The user doesn't have the passkey.
    /// </summary>
    NotFound,

    /// <summary>
    /// The passkey is the user's last, which they need to sign in.
    /// </summary>
    LastPasskey,
}
