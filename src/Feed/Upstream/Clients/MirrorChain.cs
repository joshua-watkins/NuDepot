using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Core;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;
using NuGet.Versioning;
using System.Collections.Concurrent;

namespace NuDepot.Feed.Upstream.Clients;

/// <summary>
/// The feed's mirrors, asked in the order of <see cref="MirroringOptions.Mirrors"/>. The first mirror that has a
/// package ID provides it, so a later mirror, like nuget.org after a private feed, can't add versions of a package
/// an earlier mirror has. A mirror that can't be reached, or times out, isn't asked for
/// <see cref="MirroringOptions.UnreachableMirrorMinutes"/>, and meanwhile requests skip it or stop at it, as its
/// <see cref="MirrorOptions.WhenUnreachable"/> says. Without mirroring, nothing is found.
/// </summary>
internal sealed partial class MirrorChain : IUpstreamClient
{
    /// <summary>
    /// A mirror, for the administration page.
    /// </summary>
    /// <param name="Name">The mirror's name.</param>
    /// <param name="PackageSource">The mirrored feed's service index.</param>
    /// <param name="WhenUnreachable">What requests do while it can't be reached.</param>
    /// <param name="UnreachableUntil">Until when it isn't asked because it couldn't be reached, or null if it's asked.</param>
    public sealed record MirrorStatus(string Name, Uri PackageSource, UnreachableMirrorBehavior WhenUnreachable, DateTimeOffset? UnreachableUntil);

    public MirrorChain(ILoggerFactory loggerFactory, IOptions<MirroringOptions> options, HttpClient httpClient, TimeProvider time)
    {
        ILogger<V3UpstreamClient> clientLogger = loggerFactory.CreateLogger<V3UpstreamClient>();
        MirroringOptions mirroring = options.Value;

        Logger = loggerFactory.CreateLogger<MirrorChain>();
        Time = time;
        UnreachableTime = TimeSpan.FromMinutes(mirroring.UnreachableMirrorMinutes);
        Mirrors = !mirroring.IsEnabled ? [] : [.. mirroring.Mirrors
            .Select(mirror => new Mirror(
                new V3UpstreamClient(
                    clientLogger,
                    new NuGetClient(new NuGetClientFactory(httpClient, mirror.PackageSource!.ToString())),
                    mirror.DisplayName,
                    TimeSpan.FromSeconds(mirror.RequestTimeoutSeconds),
                    TimeSpan.FromSeconds(mirror.DownloadTimeoutSeconds)),
                mirror.DisplayName,
                mirror.PackageSource!,
                mirror.WhenUnreachable))];
    }

    /// <summary>
    /// How long a package's versions from the mirrors are reused.
    /// </summary>
    internal static readonly TimeSpan ListedPackagesTime = TimeSpan.FromMinutes(1);

    // The most packages whose versions are kept at once.
    private const int MaxListedPackages = 1000;

    private ILogger Logger { get; }
    private TimeProvider Time { get; }
    private TimeSpan UnreachableTime { get; }
    private IReadOnlyList<Mirror> Mirrors { get; }
    private ConcurrentDictionary<string, Listing> ListedPackages { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether mirroring is on.
    /// </summary>
    public bool HasMirrors => Mirrors.Count > 0;

    /// <summary>
    /// The mirrors, in the order they're asked, and until when each isn't asked because it couldn't be reached.
    /// </summary>
    public IReadOnlyList<MirrorStatus> GetStatus()
    {
        DateTimeOffset now = Time.GetUtcNow();
        return [.. Mirrors.Select(mirror => new MirrorStatus(
            mirror.Name,
            mirror.PackageSource,
            mirror.WhenUnreachable,
            mirror.IsUnreachable(now) ? mirror.UnreachableUntil : null))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NuGetVersion>> ListPackageVersionsAsync(string id, CancellationToken cancellationToken)
        => await FindAsync(mirror => mirror.Client.TryListPackageVersionsAsync(id, cancellationToken), versions => versions.Count > 0) ?? [];

    /// <inheritdoc />
    public async Task<IReadOnlyList<Package>> ListPackagesAsync(string id, CancellationToken cancellationToken)
    {
        // A registration and its pages are read one after another, and restores often ask for a package at the same
        // time, so they share one lookup in the mirrors, and the pages split the same versions as the index. Each
        // request stops waiting when it's cancelled, but the lookup goes on for the others.
        Listing listing = GetListing(id, Time.GetUtcNow());
        try
        {
            return await listing.Packages.Value.WaitAsync(cancellationToken);
        }
        catch when (listing.Packages.Value.IsFaulted)
        {
            // The next request tries again.
            ListedPackages.TryRemove(new KeyValuePair<string, Listing>(id, listing));
            throw;
        }
    }

    // The package's lookup, kept for ListedPackagesTime from when it started. Expired ones are forgotten, so the cache
    // doesn't grow, and when it's full of current ones, the lookup isn't kept.
    private Listing GetListing(string id, DateTimeOffset now)
    {
        Listing started = new(now + ListedPackagesTime, new(() => ListFromMirrorsAsync(id)));
        if (ListedPackages.Count >= MaxListedPackages && !ListedPackages.ContainsKey(id))
        {
            foreach (KeyValuePair<string, Listing> entry in ListedPackages)
            {
                if (entry.Value.ExpiresAt <= now)
                    ListedPackages.TryRemove(entry);
            }

            if (ListedPackages.Count >= MaxListedPackages)
                return started;
        }

        return ListedPackages.AddOrUpdate(id, started, (_, existing) => existing.ExpiresAt > now ? existing : started);
    }

    // Not cancelled with the request that started it, since others may be waiting for it. The mirrors' request
    // timeouts end it.
    private async Task<IReadOnlyList<Package>> ListFromMirrorsAsync(string id)
        => await FindAsync(mirror => mirror.Client.TryListPackagesAsync(id, CancellationToken.None), packages => packages.Count > 0) ?? [];

    /// <inheritdoc />
    public async Task<Stream?> DownloadPackageOrNullAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        for (int i = 0; i < Mirrors.Count; i++)
        {
            Mirror mirror = Mirrors[i];
            if (mirror.IsUnreachable(Time.GetUtcNow()))
            {
                if (mirror.WhenUnreachable == UnreachableMirrorBehavior.Stop)
                    return null;
                continue;
            }

            // The last mirror is the only one left that could have the package, so it isn't asked first.
            if (i < Mirrors.Count - 1)
            {
                IReadOnlyList<NuGetVersion>? versions = await mirror.Client.TryListPackageVersionsAsync(id, cancellationToken);
                if (versions == null)
                {
                    MarkUnreachable(mirror);
                    if (mirror.WhenUnreachable == UnreachableMirrorBehavior.Stop)
                        return null;
                    continue;
                }

                if (versions.Count == 0)
                    continue;
            }

            // This mirror has the package, so no other mirror provides it.
            (Stream? package, bool failed) = await mirror.Client.TryDownloadPackageAsync(id, version, cancellationToken);
            if (failed)
                MarkUnreachable(mirror);

            return package;
        }

        return null;
    }

    // Asks each mirror in turn, until one has the package.
    private async Task<T?> FindAsync<T>(Func<Mirror, Task<T?>> ask, Func<T, bool> found) where T : class
    {
        foreach (Mirror mirror in Mirrors)
        {
            if (mirror.IsUnreachable(Time.GetUtcNow()))
            {
                if (mirror.WhenUnreachable == UnreachableMirrorBehavior.Stop)
                    return null;
                continue;
            }

            // Null means the mirror couldn't be asked.
            T? result = await ask(mirror);
            if (result == null)
            {
                MarkUnreachable(mirror);
                if (mirror.WhenUnreachable == UnreachableMirrorBehavior.Stop)
                    return null;
                continue;
            }

            if (found(result))
                return result;
        }

        return null;
    }

    private void MarkUnreachable(Mirror mirror)
    {
        if (!mirror.MarkUnreachable(Time.GetUtcNow(), UnreachableTime))
            return;

        if (mirror.WhenUnreachable == UnreachableMirrorBehavior.Stop)
        {
            Log.UnreachableStops(Logger, mirror.Name, UnreachableTime.TotalMinutes);
        }
        else
        {
            Log.UnreachableSkipped(Logger, mirror.Name, UnreachableTime.TotalMinutes);
        }
    }

    /// <summary>
    /// A package's versions from the mirrors, looked up once for the requests that ask for them meanwhile.
    /// </summary>
    /// <param name="ExpiresAt">When the versions are looked up again.</param>
    /// <param name="Packages">The lookup, which starts when it's first awaited.</param>
    private sealed record Listing(DateTimeOffset ExpiresAt, Lazy<Task<IReadOnlyList<Package>>> Packages);

    /// <summary>
    /// A mirror, and until when it's not asked because it couldn't be reached.
    /// </summary>
    private sealed class Mirror(V3UpstreamClient client, string name, Uri packageSource, UnreachableMirrorBehavior whenUnreachable)
    {
        private long unreachableUntilTicks;

        public V3UpstreamClient Client { get; } = client;
        public string Name { get; } = name;
        public Uri PackageSource { get; } = packageSource;
        public UnreachableMirrorBehavior WhenUnreachable { get; } = whenUnreachable;
        public DateTimeOffset UnreachableUntil => new(Interlocked.Read(ref unreachableUntilTicks), TimeSpan.Zero);

        public bool IsUnreachable(DateTimeOffset now) => now.UtcTicks < Interlocked.Read(ref unreachableUntilTicks);

        /// <summary>
        /// Stops asking the mirror for <paramref name="time"/>.
        /// </summary>
        /// <returns>Whether it was being asked until now, rather than another request having already stopped it.</returns>
        public bool MarkUnreachable(DateTimeOffset now, TimeSpan time)
            => Interlocked.Exchange(ref unreachableUntilTicks, (now + time).UtcTicks) <= now.UtcTicks;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Mirror {Mirror} can't be reached, so it won't be asked for {Minutes} minutes. Meanwhile, requests stop at it: packages the feed doesn't have can't be restored.")]
        public static partial void UnreachableStops(ILogger logger, string mirror, double minutes);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Mirror {Mirror} can't be reached, so it won't be asked for {Minutes} minutes. Meanwhile, requests skip it, and the mirrors after it are asked instead.")]
        public static partial void UnreachableSkipped(ILogger logger, string mirror, double minutes);
    }
}
