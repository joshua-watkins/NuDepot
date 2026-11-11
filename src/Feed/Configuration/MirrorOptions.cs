namespace NuDepot.Feed.Configuration;

/// <summary>
/// An upstream feed in <see cref="MirroringOptions.Mirrors"/>, whose packages are downloaded when they're asked for,
/// and kept.
/// </summary>
public class MirrorOptions
{
    /// <summary>
    /// nuget.org's service index.
    /// </summary>
    public static Uri NuGetPackageSource { get; } = new("https://api.nuget.org/v3/index.json");

    /// <summary>
    /// The mirror's name, for logs and errors. If unset, the package source's host name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The service index of the V3 feed that's mirrored. Required.
    /// </summary>
    public Uri? PackageSource { get; set; }

    /// <summary>
    /// The time a request to the package source may take to answer, like for its service index or a package's
    /// versions. For a download, the answer is its headers; then <see cref="DownloadTimeoutSeconds"/> applies. A
    /// mirror that doesn't answer in time counts as unreachable. Must be positive.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// The time a package's content may take to download from the package source, once it started. Must be positive.
    /// </summary>
    public int DownloadTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// What happens to requests when the mirror can't be reached, or times out: for that request, and for the
    /// <see cref="MirroringOptions.UnreachableMirrorMinutes"/> the mirror isn't asked afterwards.
    /// </summary>
    public UnreachableMirrorBehavior WhenUnreachable { get; set; } = UnreachableMirrorBehavior.Skip;

    /// <summary>
    /// The mirror's name, or its package source's host name.
    /// </summary>
    public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : PackageSource?.Host ?? "upstream";
}
