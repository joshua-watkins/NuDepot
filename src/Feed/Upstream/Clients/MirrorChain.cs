using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Core;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;
using NuGet.Versioning;

namespace NuDepot.Feed.Upstream.Clients;

/// <summary>
/// The feed's mirrors, asked in the order of <see cref="MirroringOptions.Mirrors"/>. The first mirror that has a
/// package ID provides it, so a later mirror, like nuget.org after a private feed, can't add versions of a package
/// an earlier mirror has. A mirror that can't be reached, or times out, isn't asked for
/// <see cref="MirroringOptions.UnreachableMirrorMinutes"/>, and meanwhile requests skip it or stop at it, as its
/// <see cref="MirrorOptions.WhenUnreachable"/> says. Without mirroring, nothing is found.
/// </summary>
internal sealed class MirrorChain : IUpstreamClient
{
    /// <summary>
    /// A mirror, for the administration page.
    /// </summary>
    /// <param name="Name">The mirror's name.</param>
    /// <param name="PackageSource">The mirrored feed's service index.</param>
    /// <param name="WhenUnreachable">What requests do while it can't be reached.</param>
    /// <param name="UnreachableUntil">Until when it isn't asked because it couldn't be reached, or null if it's asked.</param>
    public sealed record MirrorStatus(string Name, Uri PackageSource, UnreachableMirrorBehavior WhenUnreachable, DateTimeOffset? UnreachableUntil);

    public MirrorChain(IOptions<MirroringOptions> options, HttpClient httpClient, TimeProvider time, ILoggerFactory loggerFactory)
    {
        ILogger<V3UpstreamClient> clientLogger = loggerFactory.CreateLogger<V3UpstreamClient>();
        MirroringOptions mirroring = options.Value;

        Time = time;
        Logger = loggerFactory.CreateLogger<MirrorChain>();
        UnreachableTime = TimeSpan.FromMinutes(mirroring.UnreachableMirrorMinutes);
        Mirrors = !mirroring.IsEnabled ? [] : [.. mirroring.Mirrors
            .Select(mirror => new Mirror(
                new V3UpstreamClient(
                    new NuGetClient(new NuGetClientFactory(httpClient, mirror.PackageSource!.ToString())),
                    clientLogger,
                    mirror.DisplayName,
                    TimeSpan.FromSeconds(mirror.RequestTimeoutSeconds),
                    TimeSpan.FromSeconds(mirror.DownloadTimeoutSeconds)),
                mirror.DisplayName,
                mirror.PackageSource!,
                mirror.WhenUnreachable))];
    }

    private TimeProvider Time { get; }
    private ILogger<MirrorChain> Logger { get; }
    private TimeSpan UnreachableTime { get; }
    private IReadOnlyList<Mirror> Mirrors { get; }

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
        => await FindAsync(mirror => mirror.Client.TryListPackagesAsync(id, cancellationToken), packages => packages.Count > 0) ?? [];

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
            Logger.LogWarning(
                "Mirror {Mirror} can't be reached, so it won't be asked for {Minutes} minutes. Meanwhile, requests stop at it: packages the feed doesn't have can't be restored.",
                mirror.Name,
                UnreachableTime.TotalMinutes);
        }
        else
        {
            Logger.LogWarning(
                "Mirror {Mirror} can't be reached, so it won't be asked for {Minutes} minutes. Meanwhile, requests skip it, and the mirrors after it are asked instead.",
                mirror.Name,
                UnreachableTime.TotalMinutes);
        }
    }

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
}
