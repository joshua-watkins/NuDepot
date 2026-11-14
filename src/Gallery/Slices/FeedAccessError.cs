namespace NuDepot.Gallery.Slices;

/// <summary>
/// Why the website couldn't read the feed.
/// </summary>
internal enum FeedAccessError
{
    /// <summary>
    /// The feed couldn't be reached, or returned something unexpected.
    /// </summary>
    None,

    /// <summary>
    /// The feed needs the visitor to sign in.
    /// </summary>
    SignInRequired,

    /// <summary>
    /// The visitor is signed in, but their account can't read the feed.
    /// </summary>
    Forbidden,
}
