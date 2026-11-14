namespace NuDepot.Feed.Endpoints;

/// <summary>
/// A mirror, and whether it can be reached.
/// </summary>
internal class MirrorResponse
{
    /// <summary>
    /// The mirror's name.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// The mirrored feed's service index.
    /// </summary>
    public string PackageSource { get; set; } = null!;

    /// <summary>
    /// What requests do while the mirror can't be reached: "Skip" or "Stop".
    /// </summary>
    public string WhenUnreachable { get; set; } = null!;

    /// <summary>
    /// Until when the mirror isn't asked, because it couldn't be reached. Null while it's asked.
    /// </summary>
    public DateTimeOffset? UnreachableUntil { get; set; }
}
