namespace NuDepot.Gallery.Slices;

/// <summary>
/// The model of <see cref="FeedError"/>, shown when the website couldn't get the page's content from the feed.
/// </summary>
/// <param name="PathBase">The application's path base, which links start with.</param>
/// <param name="ServiceIndexUrl">The feed the website reads from.</param>
/// <param name="Access">Whether the feed refused the visitor, rather than being unreachable.</param>
/// <param name="SignInUrl">Where the visitor can sign in to read the feed, if they can.</param>
public record FeedErrorModel(string PathBase, string ServiceIndexUrl, FeedAccessError Access = FeedAccessError.None, string? SignInUrl = null);

/// <summary>
/// Why the website couldn't read the feed.
/// </summary>
public enum FeedAccessError
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
