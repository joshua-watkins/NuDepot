namespace NuDepot.Feed.Extensions;

/// <summary>
/// What the application's request pipeline uses of the feed, set by <c>UseFeed()</c>,
/// <c>UseApiKeys()</c> and <c>UsePasskeys()</c>.
/// </summary>
public sealed class FeedPipeline
{
    /// <summary>
    /// Whether the application maps the feed's endpoints.
    /// </summary>
    public bool UsesFeed { get; internal set; }

    /// <summary>
    /// Whether the application accepts API keys. Without it, requests are anonymous.
    /// </summary>
    public bool UsesApiKeys { get; internal set; }

    /// <summary>
    /// Whether the application uses the feed's users, who sign in with passkeys.
    /// </summary>
    public bool UsesPasskeys { get; internal set; }
}
