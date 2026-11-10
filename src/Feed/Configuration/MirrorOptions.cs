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
    /// The time a request to the package source, like a download, may take. Must be positive.
    /// </summary>
    public int PackageDownloadTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// The mirror's name, or its package source's host name.
    /// </summary>
    public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : PackageSource?.Host ?? "upstream";
}
