namespace NuDepot.Feed.Configuration;

/// <summary>
/// Mirroring, or read-through caching: packages that aren't on the feed are downloaded from upstream feeds when
/// they're asked for, and kept. The options are bound from the <see cref="Section"/> configuration section only once
/// the application adds mirroring, with <c>AddMirroring()</c> or <c>EnableMirroring()</c>.
/// </summary>
public class MirroringOptions
{
    /// <summary>
    /// The configuration section the options are bound from.
    /// </summary>
    public const string Section = "Feed:Mirroring";

    /// <summary>
    /// Whether the feed mirrors <see cref="Mirrors"/>. True by default with <c>AddMirroring()</c>, and false by
    /// default with <c>EnableMirroring()</c>. Either way, the configuration can change it. Without either, it's
    /// false, and the configuration is ignored.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// The upstream feeds, asked in this order. The first one that has a package ID provides all of its versions,
    /// so a later mirror can't add versions of a package an earlier mirror has: put private feeds before public
    /// ones. A mirror that can't be reached is skipped, or stops the chain; see
    /// <see cref="MirrorOptions.WhenUnreachable"/>. Mirroring that's on needs at least
    /// one, like nuget.org's, <see cref="MirrorOptions.NuGetPackageSource"/>.
    /// </summary>
    public List<MirrorOptions> Mirrors { get; set; } = [];

    /// <summary>
    /// How long a mirror that couldn't be reached, or timed out, isn't asked, before it's tried again. Its
    /// <see cref="MirrorOptions.WhenUnreachable"/> decides what happens to requests meanwhile. Must be positive.
    /// </summary>
    public int UnreachableMirrorMinutes { get; set; } = 5;

    /// <summary>
    /// How many times a second the mirrors may be asked, together, for packages the feed doesn't have, so busy
    /// clients can't get the feed's address throttled by a mirror like nuget.org. Ten seconds' worth may be asked at
    /// once. Packages the feed has, and versions looked up in the last minute, don't count. Beyond it, requests that
    /// need the mirrors get 429 Too Many Requests. 0 for no limit.
    /// </summary>
    public int MaxLookupsPerSecond { get; set; } = 50;
}
