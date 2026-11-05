using NuGet.Versioning;
using System.Collections.Concurrent;

namespace NuDepot.Feed.Signing;

/// <summary>
/// What <see cref="PackageResigner"/> shares between downloads: a lock for each package, so a download changes a
/// package once while others wait, and when each mirrored package was last downloaded from upstream again. Each feed
/// has its own, so feeds hosted in the same process don't wait for each other.
/// </summary>
public sealed class PackageResignerLocks : IDisposable
{
    // Packages share these locks by their hash, so the locks don't grow with the feed.
    private SemaphoreSlim[] Locks { get; } = [.. Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1))];

    private ConcurrentDictionary<string, DateTimeOffset> UpstreamAttempts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the lock for a package.
    /// </summary>
    /// <param name="id">The package's ID.</param>
    /// <param name="version">The package's version.</param>
    /// <returns>The lock, which other packages may share.</returns>
    public SemaphoreSlim For(string id, NuGetVersion version)
        => Locks[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(Key(id, version)) % Locks.Length];

    /// <summary>
    /// Whether a mirrored package was downloaded from upstream again recently.
    /// </summary>
    /// <param name="id">The package's ID.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="now">The current time.</param>
    /// <param name="interval">How long ago counts as recently.</param>
    /// <returns>True if it was downloaded within <paramref name="interval"/>.</returns>
    public bool WasRefreshedRecently(string id, NuGetVersion version, DateTimeOffset now, TimeSpan interval)
        => UpstreamAttempts.TryGetValue(Key(id, version), out DateTimeOffset attempted) && now - attempted < interval;

    /// <summary>
    /// Records that a mirrored package is being downloaded from upstream again.
    /// </summary>
    /// <param name="id">The package's ID.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="now">The current time.</param>
    public void RecordRefresh(string id, NuGetVersion version, DateTimeOffset now)
        => UpstreamAttempts[Key(id, version)] = now;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (SemaphoreSlim packageLock in Locks)
            packageLock.Dispose();
    }

    private static string Key(string id, NuGetVersion version) => $"{id}/{version.ToNormalizedString()}";
}
