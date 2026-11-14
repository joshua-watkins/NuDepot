using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Search;
using NuDepot.Feed.Signing;
using NuDepot.Feed.Storage;
using NuGet.Packaging;
using NuGet.Versioning;

namespace NuDepot.Feed.Indexing;

/// <summary>
/// Adds pushed packages to the feed: stores them, signs them, and records them in the database, the search index and the catalog.
/// </summary>
/// <param name="logger">The logger.</param>
/// <param name="packages">The package database.</param>
/// <param name="storage">Where packages are stored.</param>
/// <param name="search">The search index.</param>
/// <param name="catalog">The catalog that records the change.</param>
/// <param name="signing">Signs packages with the feed's repository certificate.</param>
/// <param name="url">Generates the feed's URLs.</param>
/// <param name="time">The current time.</param>
/// <param name="locks">The packages' locks, so a version is pushed once at a time.</param>
/// <param name="options">The feed's options.</param>
/// <param name="httpContextAccessor">The request, whose client is logged with what it changes.</param>
internal partial class PackageIndexingService(ILogger<PackageIndexingService> logger, IPackageDatabase packages, IPackageStorageService storage, ISearchIndexer search, ICatalogService catalog, IRepositorySigningService signing, IUrlGenerator url, TimeProvider time, PackageLocks locks, IOptionsSnapshot<FeedOptions> options, IHttpContextAccessor httpContextAccessor) : IPackageIndexingService
{
    private ILogger Logger { get; } = logger;
    private IPackageDatabase Packages { get; } = packages;
    private IPackageStorageService Storage { get; } = storage;
    private ISearchIndexer Search { get; } = search;
    private ICatalogService Catalog { get; } = catalog;
    private IRepositorySigningService Signing { get; } = signing;
    private IUrlGenerator Url { get; } = url;
    private TimeProvider Time { get; } = time;
    private PackageLocks Locks { get; } = locks;
    private IOptionsSnapshot<FeedOptions> Options { get; } = options;
    private IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;

    // Who's changing the feed, for the logs.
    private string Actor => FeedActor.Describe(HttpContextAccessor.HttpContext);

    /// <inheritdoc />
    public Task<PackageIndexingResult> IndexAsync(Stream packageStream, CancellationToken cancellationToken)
        => IndexAsync(packageStream, allowOverwrite: true, cancellationToken);

    /// <inheritdoc />
    public async Task<PackageIndexingResult> IndexAsync(Stream packageStream, bool allowOverwrite, CancellationToken cancellationToken)
    {
        using PackageContents? contents = await ReadOrNullAsync(packageStream, cancellationToken);
        if (contents == null)
            return PackageIndexingResult.InvalidPackage;

        PackageIndexingResult result = await IndexWithLockAsync(packageStream, contents, allowOverwrite, cancellationToken);
        if (result == PackageIndexingResult.Success)
            Log.Pushed(Logger, Actor, contents.Package.Id, contents.Package.NormalizedVersionString);

        return result;
    }

    /// <inheritdoc />
    public async Task<PackageIndexingResult> MirrorAsync(Stream packageStream, string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        using PackageContents? contents = await ReadOrNullAsync(packageStream, cancellationToken);
        if (contents == null)
            return PackageIndexingResult.InvalidPackage;

        // A mirror can only add the package it was asked for, not another ID or version.
        Package package = contents.Package;
        if (!string.Equals(package.Id, id, StringComparison.OrdinalIgnoreCase) || package.Version != version)
        {
            Log.MirrorSentAnotherPackage(Logger, id, version, package.Id, package.NormalizedVersionString);
            return PackageIndexingResult.InvalidPackage;
        }

        package.IsMirrored = true;
        return await IndexWithLockAsync(packageStream, contents, allowOverwrite: false, cancellationToken);
    }

    private async Task<PackageIndexingResult> IndexWithLockAsync(Stream packageStream, PackageContents contents, bool allowOverwrite, CancellationToken cancellationToken)
    {
        // Pushes of the same version wait for each other, so the second finds the first's package.
        Package package = contents.Package;
        SemaphoreSlim packageLock = Locks.For(package.Id, package.Version);
        await packageLock.WaitAsync(cancellationToken);
        try
        {
            return await IndexAsync(packageStream, contents, allowOverwrite, cancellationToken);
        }
        finally
        {
            packageLock.Release();
        }
    }

    private async Task<PackageIndexingResult> IndexAsync(Stream packageStream, PackageContents contents, bool allowOverwrite, CancellationToken cancellationToken)
    {
        Package package = contents.Package;

        if (await Packages.ExistsAsync(package.Id, package.Version, cancellationToken))
        {
            if (!Options.Value.AllowPackageOverwrites || !allowOverwrite)
                return PackageIndexingResult.PackageAlreadyExists;

            Log.Replacing(Logger, Actor, package.Id, package.NormalizedVersionString);
            await Packages.HardDeletePackageAsync(package.Id, package.Version, cancellationToken);
            await Storage.DeleteAsync(package.Id, package.Version, cancellationToken);
            await Catalog.AddPackageDeleteAsync(package.Id, package.Version, cancellationToken);
        }
        else
        {
            // The database decides which packages the feed has, so any files already stored for this version are
            // left from a push that failed before it was recorded.
            await Storage.DeleteAsync(package.Id, package.Version, cancellationToken);
        }

        Log.Validated(Logger, package.Id, package.NormalizedVersionString);

        RepositorySigningResult signing = Signing.IsEnabled
            ? await Signing.SignAsync(packageStream, Url.GetServiceIndexUrl(), cancellationToken)
            : RepositorySigningResult.Unsigned;

        using (signing.SignedPackage)
        {
            Stream storedPackageStream = signing.SignedPackage ?? packageStream;

            // Record the repository signature, this server's or another repository's, like nuget.org's.
            package.IsRepositorySigned = signing.IsRepositorySigned;
            RepositorySignatureDetails? signature = await Signing.GetRepositorySignatureAsync(storedPackageStream, cancellationToken);
            package.RepositorySignatureFingerprint = signature?.Fingerprint;
            package.RepositorySignatureExpiresAt = signature?.ExpiresAt?.UtcDateTime;

            // The catalog describes the package as stored, including any repository signature.
            storedPackageStream.Position = 0;
            (package.PackageHash, package.PackageSize) = await storedPackageStream.ComputeSha512Async(cancellationToken);
            storedPackageStream.Position = 0;

            try
            {
                await Storage.SavePackageContentAsync(
                    package,
                    storedPackageStream,
                    contents.Nuspec,
                    contents.Readme,
                    contents.Icon,
                    contents.License,
                    cancellationToken);
            }
            catch (Exception e)
            {
                Log.StorageFailed(Logger, e, package.Id, package.NormalizedVersionString);

                await DeleteOrphanedContentAsync(package);
                throw;
            }
        }

        Log.Stored(Logger, package.Id, package.NormalizedVersionString);

        PackageAddResult result;
        try
        {
            result = await Packages.AddAsync(package, cancellationToken);
        }
        catch
        {
            await DeleteOrphanedContentAsync(package);
            throw;
        }

        if (result == PackageAddResult.PackageAlreadyExists)
        {
            // Another process added it. Its files are the same as these, or this push's files lost to its.
            Log.AlreadyExists(Logger, package.Id, package.NormalizedVersionString);

            return PackageIndexingResult.PackageAlreadyExists;
        }

        if (result != PackageAddResult.Success)
        {
            Log.UnknownAddResult(Logger, result);

            throw new InvalidOperationException($"Unknown {nameof(PackageAddResult)} value: {result}");
        }

        await Catalog.AddPackageDetailsAsync([package], cancellationToken);

        Log.SavedToDatabase(Logger, package.Id, package.NormalizedVersionString);

        await Search.IndexAsync(package, cancellationToken);

        Log.Indexed(Logger, package.Id, package.NormalizedVersionString);

        return PackageIndexingResult.Success;
    }

    // Reads what the feed stores from a package, or null if it isn't a valid package. The files are copied, so they
    // can be stored after the package is signed.
    private async Task<PackageContents?> ReadOrNullAsync(Stream packageStream, CancellationToken cancellationToken)
    {
        PackageContents? contents = null;
        try
        {
            using PackageArchiveReader packageReader = new(packageStream, leaveStreamOpen: true);

            // Entries can't leave the package's folder when it's extracted, as NuGet does on restore.
            await packageReader.ValidatePackageEntriesAsync(cancellationToken);

            Package package = packageReader.GetPackageMetadata();
            package.PublishedAt = Time.GetUtcNow().UtcDateTime;
            contents = new PackageContents(package)
            {
                Nuspec = await CopyAsync(await packageReader.GetNuspecAsync(cancellationToken)),
            };

            if (package.HasReadme)
                contents.Readme = await CopyAsync(await packageReader.GetReadmeAsync(cancellationToken));

            if (package.HasEmbeddedIcon)
                contents.Icon = await CopyAsync(await packageReader.GetIconAsync(cancellationToken));

            if (package.HasEmbeddedLicense)
            {
                Stream license = await packageReader.GetLicenseAsync(cancellationToken)
                    ?? throw new InvalidDataException("The package's license file is missing.");
                contents.License = await CopyAsync(license);
            }

            return contents;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.InvalidPackage(Logger, e);

            contents?.Dispose();
            return null;
        }
        catch
        {
            contents?.Dispose();
            throw;
        }

        async Task<Stream> CopyAsync(Stream entry)
        {
            using (entry)
                return await entry.AsTemporaryFileStreamAsync(cancellationToken);
        }
    }

    // Removes what a failed push stored, even if the push was canceled, so the version can be pushed again.
    private async Task DeleteOrphanedContentAsync(Package package)
    {
        try
        {
            await Storage.DeleteAsync(package.Id, package.Version, CancellationToken.None);
        }
        catch (Exception e)
        {
            Log.OrphanedContentNotDeleted(Logger, e, package.Id, package.NormalizedVersionString);
        }
    }

    // A package's metadata and the files the feed serves from it, in temporary files that are deleted when they're disposed.
    private sealed class PackageContents(Package package) : IDisposable
    {
        public Package Package { get; } = package;
        public Stream Nuspec { get; set; } = Stream.Null;
        public Stream? Readme { get; set; }
        public Stream? Icon { get; set; }
        public Stream? License { get; set; }

        public void Dispose()
        {
            Nuspec.Dispose();
            Readme?.Dispose();
            Icon?.Dispose();
            License?.Dispose();
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "A mirror was asked for package {PackageId} {PackageVersion}, but sent {MirroredId} {MirroredVersion}, so it isn't added")]
        public static partial void MirrorSentAnotherPackage(ILogger logger, string packageId, NuGetVersion packageVersion, string mirroredId, string mirroredVersion);

        [LoggerMessage(Level = LogLevel.Information, Message = "Validated package {PackageId} {PackageVersion}, persisting content to storage...")]
        public static partial void Validated(ILogger logger, string packageId, string packageVersion);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist package {PackageId} {PackageVersion} content to storage")]
        public static partial void StorageFailed(ILogger logger, Exception exception, string packageId, string packageVersion);

        [LoggerMessage(Level = LogLevel.Information, Message = "Persisted package {Id} {Version} content to storage, saving metadata to database...")]
        public static partial void Stored(ILogger logger, string id, string version);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Package {Id} {Version} metadata already exists in database")]
        public static partial void AlreadyExists(ILogger logger, string id, string version);

        [LoggerMessage(Level = LogLevel.Error, Message = $"Unknown {nameof(PackageAddResult)} value: {{PackageAddResult}}")]
        public static partial void UnknownAddResult(ILogger logger, PackageAddResult packageAddResult);

        [LoggerMessage(Level = LogLevel.Information, Message = "Successfully persisted package {Id} {Version} metadata to database. Indexing in search...")]
        public static partial void SavedToDatabase(ILogger logger, string id, string version);

        [LoggerMessage(Level = LogLevel.Information, Message = "Successfully indexed package {Id} {Version} in search")]
        public static partial void Indexed(ILogger logger, string id, string version);

        [LoggerMessage(Level = LogLevel.Error, Message = "Uploaded package is invalid")]
        public static partial void InvalidPackage(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Actor} pushed package {PackageId} {PackageVersion}")]
        public static partial void Pushed(ILogger logger, string actor, string packageId, string packageVersion);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Actor} is replacing package {PackageId} {PackageVersion}, which is deleted permanently")]
        public static partial void Replacing(ILogger logger, string actor, string packageId, string packageVersion);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to delete the stored content of package {PackageId} {PackageVersion} after its push failed")]
        public static partial void OrphanedContentNotDeleted(ILogger logger, Exception exception, string packageId, string packageVersion);
    }
}
