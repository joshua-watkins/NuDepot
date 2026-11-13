using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Storage;
using NuDepot.Feed.Upstream;
using NuDepot.Feed.Upstream.Clients;
using NuGet.Packaging;
using NuGet.Versioning;

namespace NuDepot.Feed.Signing;

/// <summary>
/// Keeps the repository signatures of downloaded packages current, before they're served:
/// <list type="bullet">
/// <item>A package this server signed with a certificate other than the current one, like last year's generated
/// certificate, is re-signed with the current one.</item>
/// <item>A package mirrored from another repository, whose signature expired, is downloaded from its mirrors
/// again, in case it was re-signed there, like by another NuDepot feed with generated certificates.</item>
/// </list>
/// The stored package is replaced, and its hash, size and catalog entry are updated. Unsigned packages aren't changed.
/// </summary>
/// <param name="signing">Signs the packages.</param>
/// <param name="context">The package database.</param>
/// <param name="packages">Finds the packages, with what the catalog needs.</param>
/// <param name="storage">Where packages are stored.</param>
/// <param name="catalog">The catalog that records the change.</param>
/// <param name="upstream">The repository mirrored packages come from.</param>
/// <param name="url">Generates the service index URL that the signatures record.</param>
/// <param name="time">The current time, which signatures expire by.</param>
/// <param name="locks">The feed's package locks, and when mirrored packages were last downloaded again.</param>
/// <param name="logger">The logger.</param>
public partial class PackageResigner(IRepositorySigningService signing, NuDepotContext context, IPackageDatabase packages, IPackageStorageService storage, ICatalogService catalog, IUpstreamClient upstream, IUrlGenerator url, TimeProvider time, PackageLocks locks, ILogger<PackageResigner> logger)
{
    // A mirrored package whose upstream copy is also expired is only downloaded again after this long.
    private static readonly TimeSpan UpstreamRetryInterval = TimeSpan.FromHours(1);

    private IRepositorySigningService Signing { get; } = signing;
    private NuDepotContext Context { get; } = context;
    private IPackageDatabase Packages { get; } = packages;
    private IPackageStorageService Storage { get; } = storage;
    private ICatalogService Catalog { get; } = catalog;
    private IUpstreamClient Upstream { get; } = upstream;
    private IUrlGenerator Url { get; } = url;
    private TimeProvider Time { get; } = time;
    private PackageLocks Locks { get; } = locks;
    private ILogger<PackageResigner> Logger { get; } = logger;

    // An upstream client other than the feed's mirror chain is taken to mirror something.
    private bool IsMirroring => Upstream is not MirrorChain chain || chain.HasMirrors;

    /// <summary>
    /// Makes a package that's being downloaded carry a current repository signature, if it has one.
    /// </summary>
    /// <param name="id">The package's ID.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="content">The stored package.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package to serve: <paramref name="content"/>, or a new copy, in which case <paramref name="content"/> is disposed.</returns>
    public async Task<Stream> RefreshAsync(string id, NuGetVersion version, Stream content, CancellationToken cancellationToken)
    {
        SignatureState? state = await GetStateAsync(id, version, content, cancellationToken);
        if (state == null)
            return content;

        if (state.IsOurs)
        {
            string? current = Signing.IsEnabled ? await Signing.GetSigningFingerprintAsync(cancellationToken) : null;
            if (current == null || string.Equals(state.Fingerprint, current, StringComparison.OrdinalIgnoreCase))
                return content;

            return await WithLockAsync(id, version, async () =>
            {
                // Another download may have re-signed it while this one waited.
                SignatureState? latest = await GetStateAsync(id, version, content, cancellationToken);
                if (latest == null || string.Equals(latest.Fingerprint, current, StringComparison.OrdinalIgnoreCase))
                    return await ReopenAsync(id, version, content, cancellationToken);

                return await ResignAsync(id, version, content, cancellationToken);
            });
        }

        if (!IsMirroring || !state.IsMirrored || state.ExpiresAt == null || state.ExpiresAt > Time.GetUtcNow().UtcDateTime)
            return content;

        DateTimeOffset now = Time.GetUtcNow();
        if (Locks.WasRefreshedRecently(id, version, now, UpstreamRetryInterval))
            return content;

        return await WithLockAsync(id, version, async () =>
        {
            if (Locks.WasRefreshedRecently(id, version, now, UpstreamRetryInterval))
                return await ReopenAsync(id, version, content, cancellationToken);

            Locks.RecordRefresh(id, version, now);
            return await RefreshFromUpstreamAsync(id, version, content, state.Fingerprint, cancellationToken);
        });
    }

    private async Task<SignatureState?> GetStateAsync(string id, NuGetVersion version, Stream content, CancellationToken cancellationToken)
    {
        string normalizedVersion = version.ToNormalizedString();
        var row = await Context.Packages
            .Where(p => p.Id == id && p.NormalizedVersionString == normalizedVersion)
            .Select(p => new { p.IsRepositorySigned, p.IsMirrored, p.RepositorySignatureFingerprint, p.RepositorySignatureExpiresAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (row == null)
            return null;

        if (row.RepositorySignatureFingerprint != null)
            return new(row.IsRepositorySigned, row.IsMirrored, row.RepositorySignatureFingerprint, row.RepositorySignatureExpiresAt);

        // Packages stored before the signature was recorded: read it from the package, once.
        RepositorySignatureDetails? signature = await Signing.GetRepositorySignatureAsync(content, cancellationToken);
        if (signature == null)
            return null;

        DateTime? expiresAt = signature.ExpiresAt?.UtcDateTime;
        await Context.Packages
            .Where(p => p.Id == id && p.NormalizedVersionString == normalizedVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.RepositorySignatureFingerprint, signature.Fingerprint)
                    .SetProperty(p => p.RepositorySignatureExpiresAt, expiresAt),
                cancellationToken);

        return new(row.IsRepositorySigned, row.IsMirrored, signature.Fingerprint, expiresAt);
    }

    private async Task<Stream> WithLockAsync(string id, NuGetVersion version, Func<Task<Stream>> action)
    {
        SemaphoreSlim packageLock = Locks.For(id, version);
        await packageLock.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            packageLock.Release();
        }
    }

    // The package may have been replaced while this download waited, so serve the stored package again.
    private async Task<Stream> ReopenAsync(string id, NuGetVersion version, Stream content, CancellationToken cancellationToken)
    {
        Stream? stored = await Storage.GetPackageStreamAsync(id, version, cancellationToken);
        if (stored == null)
            return content;

        content.Dispose();
        return stored;
    }

    private async Task<Stream> ResignAsync(string id, NuGetVersion version, Stream content, CancellationToken cancellationToken)
    {
        RepositorySigningResult result = await Signing.ResignAsync(content, Url.GetServiceIndexUrl(), cancellationToken);
        if (result.SignedPackage == null)
            return content;

        Stream? replaced = await ReplaceAsync(id, version, content, result.SignedPackage, isRepositorySigned: true, cancellationToken);
        if (replaced == null)
            return content;

        Log.Resigned(Logger, id, version, result.Fingerprint);
        return replaced;
    }

    private async Task<Stream> RefreshFromUpstreamAsync(string id, NuGetVersion version, Stream content, string fingerprint, CancellationToken cancellationToken)
    {
        Stream? fresh;
        try
        {
            fresh = await Upstream.DownloadPackageOrNullAsync(id, version, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.RefreshDownloadFailed(Logger, e, id, version);
            return content;
        }

        if (fresh == null)
            return content;

        RepositorySignatureDetails? signature = await Signing.GetRepositorySignatureAsync(fresh, cancellationToken);
        if (signature == null || string.Equals(signature.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            Log.NotResignedUpstream(Logger, id, version);
            fresh.Dispose();
            return content;
        }

        // Only the signature may change: the package's content hash leaves the signature out.
        if (ContentHash(content) != ContentHash(fresh))
        {
            Log.UpstreamContentDiffers(Logger, id, version);
            fresh.Dispose();
            return content;
        }

        Stream? replaced = await ReplaceAsync(id, version, content, fresh, isRepositorySigned: false, cancellationToken);
        if (replaced == null)
            return content;

        Log.Refreshed(Logger, id, version, signature.Fingerprint);
        return replaced;
    }

    // Stores the new copy, then updates the package's hash, size, signature and catalog entry. Returns null, and
    // disposes the new copy, if it couldn't be stored.
    private async Task<Stream?> ReplaceAsync(string id, NuGetVersion version, Stream content, Stream replacement, bool isRepositorySigned, CancellationToken cancellationToken)
    {
        string hash;
        long size;
        try
        {
            replacement.Position = 0;
            (hash, size) = await replacement.ComputeSha512Async(cancellationToken);

            replacement.Position = 0;
            await Storage.ReplacePackageStreamAsync(id, version, replacement, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Like when the storage is read only. Serve the package as it is.
            Log.ReplaceFailed(Logger, e, id, version);
            replacement.Dispose();
            content.Position = 0;
            return null;
        }

        RepositorySignatureDetails? signature = await Signing.GetRepositorySignatureAsync(replacement, cancellationToken);
        string? fingerprint = signature?.Fingerprint;
        DateTime? expiresAt = signature?.ExpiresAt?.UtcDateTime;
        bool updated = await Packages.UpdateAsync(
            id,
            version,
            setters => setters
                .SetProperty(p => p.PackageHash, hash)
                .SetProperty(p => p.PackageSize, size)
                .SetProperty(p => p.IsRepositorySigned, isRepositorySigned)
                .SetProperty(p => p.RepositorySignatureFingerprint, fingerprint)
                .SetProperty(p => p.RepositorySignatureExpiresAt, expiresAt),
            cancellationToken);

        // The catalog describes the package as stored, including its new hash.
        Package? package = updated ? await Packages.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken) : null;
        if (package != null)
            await Catalog.AddPackageDetailsAsync([package], cancellationToken);

        content.Dispose();
        replacement.Position = 0;
        return replacement;
    }

    private static string ContentHash(Stream package)
    {
        package.Position = 0;
        try
        {
            using PackageArchiveReader reader = new(package, leaveStreamOpen: true);
            return reader.GetContentHash(CancellationToken.None);
        }
        finally
        {
            package.Position = 0;
        }
    }

    /// <summary>
    /// A package's repository signature, as recorded.
    /// </summary>
    /// <param name="IsOurs">Whether this server made it.</param>
    /// <param name="IsMirrored">Whether the package came from a mirror, which may have re-signed it.</param>
    /// <param name="Fingerprint">The fingerprint of the certificate that made it.</param>
    /// <param name="ExpiresAt">When it stops being valid, or null if it's timestamped.</param>
    private sealed record SignatureState(bool IsOurs, bool IsMirrored, string Fingerprint, DateTime? ExpiresAt);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Re-signed package {PackageId} {PackageVersion} with the certificate {Fingerprint}")]
        public static partial void Resigned(ILogger logger, string packageId, NuGetVersion packageVersion, string? fingerprint);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to download package {PackageId} {PackageVersion} from upstream to refresh its expired signature")]
        public static partial void RefreshDownloadFailed(ILogger logger, Exception exception, string packageId, NuGetVersion packageVersion);

        [LoggerMessage(Level = LogLevel.Information, Message = "Upstream hasn't re-signed package {PackageId} {PackageVersion} yet. It's served with its expired signature")]
        public static partial void NotResignedUpstream(ILogger logger, string packageId, NuGetVersion packageVersion);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream's copy of package {PackageId} {PackageVersion} has different content, so it isn't used")]
        public static partial void UpstreamContentDiffers(ILogger logger, string packageId, NuGetVersion packageVersion);

        [LoggerMessage(Level = LogLevel.Information, Message = "Refreshed package {PackageId} {PackageVersion} from upstream, which re-signed it with the certificate {Fingerprint}")]
        public static partial void Refreshed(ILogger logger, string packageId, NuGetVersion packageVersion, string fingerprint);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to store the new copy of package {PackageId} {PackageVersion}. It's served as it was")]
        public static partial void ReplaceFailed(ILogger logger, Exception exception, string packageId, NuGetVersion packageVersion);
    }
}
