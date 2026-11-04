namespace NuDepot.Feed.Upstream;

/// <summary>
/// Where package download counts come from.
/// </summary>
public interface IPackageDownloadsSource
{
    /// <summary>
    /// Gets every package's download counts.
    /// </summary>
    /// <returns>Download counts keyed by lowercase package ID, then by lowercase normalized version.</returns>
    Task<Dictionary<string, Dictionary<string, long>>> GetPackageDownloadsAsync();
}
