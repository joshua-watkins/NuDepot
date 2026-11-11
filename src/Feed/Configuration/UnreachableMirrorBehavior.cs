namespace NuDepot.Feed.Configuration;

/// <summary>
/// What happens to requests when a mirror can't be reached.
/// </summary>
public enum UnreachableMirrorBehavior
{
    /// <summary>
    /// The later mirrors are asked instead, so packages they have still work. But a later mirror, like nuget.org,
    /// can then provide a package whose real home is the unreachable mirror.
    /// </summary>
    Skip,

    /// <summary>
    /// The later mirrors aren't asked either, so packages the feed hasn't kept can't be restored until the mirror is
    /// back. Use it for private feeds, so nuget.org can't provide a package with the same ID as yours.
    /// </summary>
    Stop,
}
