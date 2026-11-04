using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Search;
using NuDepot.Feed.Signing;
using NuDepot.Feed.Storage;
using NuGet.Packaging;

namespace NuDepot.Feed.Indexing;

/// <summary>
/// Adds pushed packages to the feed: stores them, signs them, and records them in the database, the search index and the catalog.
/// </summary>
/// <param name="packages">The package database.</param>
/// <param name="storage">Where packages are stored.</param>
/// <param name="search">The search index.</param>
/// <param name="catalog">The catalog that records the change.</param>
/// <param name="signing">Signs packages with the feed's repository certificate.</param>
/// <param name="url">Generates the feed's URLs.</param>
/// <param name="time">The current time.</param>
/// <param name="options">The feed's options.</param>
/// <param name="logger">The logger.</param>
public class PackageIndexingService(IPackageDatabase packages, IPackageStorageService storage, ISearchIndexer search, ICatalogService catalog, IRepositorySigningService signing, IUrlGenerator url, SystemTime time, IOptionsSnapshot<FeedOptions> options, ILogger<PackageIndexingService> logger) : IPackageIndexingService
{
    private IPackageDatabase Packages { get; } = packages;
    private IPackageStorageService Storage { get; } = storage;
    private ISearchIndexer Search { get; } = search;
    private ICatalogService Catalog { get; } = catalog;
    private IRepositorySigningService Signing { get; } = signing;
    private IUrlGenerator Url { get; } = url;
    private SystemTime Time { get; } = time;
    private IOptionsSnapshot<FeedOptions> Options { get; } = options;
    private ILogger<PackageIndexingService> Logger { get; } = logger;

    /// <inheritdoc />
    public Task<PackageIndexingResult> IndexAsync(Stream packageStream, CancellationToken cancellationToken)
        => IndexAsync(packageStream, allowOverwrite: true, cancellationToken);

    /// <inheritdoc />
    public async Task<PackageIndexingResult> IndexAsync(Stream packageStream, bool allowOverwrite, CancellationToken cancellationToken)
    {
        // Try to extract all the necessary information from the package.
        Package package;
        Stream nuspecStream;
        Stream? readmeStream;
        Stream? iconStream;
        Stream? licenseStream;

        try
        {
            using (PackageArchiveReader packageReader = new(packageStream, leaveStreamOpen: true))
            {
                package = packageReader.GetPackageMetadata();
                package.PublishedAt = Time.UtcNow;

                nuspecStream = await packageReader.GetNuspecAsync(cancellationToken);
                nuspecStream = await nuspecStream.AsTemporaryFileStreamAsync(cancellationToken);

                if (package.HasReadme)
                {
                    readmeStream = await packageReader.GetReadmeAsync(cancellationToken);
                    readmeStream = await readmeStream.AsTemporaryFileStreamAsync(cancellationToken);
                }
                else
                {
                    readmeStream = null;
                }

                if (package.HasEmbeddedIcon)
                {
                    iconStream = await packageReader.GetIconAsync(cancellationToken);
                    iconStream = await iconStream.AsTemporaryFileStreamAsync(cancellationToken);
                }
                else
                {
                    iconStream = null;
                }

                if (package.HasEmbeddedLicense)
                {
                    Stream license = await packageReader.GetLicenseAsync(cancellationToken)
                        ?? throw new InvalidOperationException("Package does not have a license file!");
                    licenseStream = await license.AsTemporaryFileStreamAsync(cancellationToken);
                }
                else
                {
                    licenseStream = null;
                }
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Uploaded package is invalid");

            return PackageIndexingResult.InvalidPackage;
        }

        // The package is well-formed. Ensure this is a new package.
        if (await Packages.ExistsAsync(package.Id, package.Version, cancellationToken))
        {
            if (!Options.Value.AllowPackageOverwrites || !allowOverwrite)
                return PackageIndexingResult.PackageAlreadyExists;

            await Packages.HardDeletePackageAsync(package.Id, package.Version, cancellationToken);
            await Storage.DeleteAsync(package.Id, package.Version, cancellationToken);
            await Catalog.AddPackageDeleteAsync(package.Id, package.Version, cancellationToken);
        }

        // TODO: Add more package validations
        // TODO: Call PackageArchiveReader.ValidatePackageEntriesAsync
        Logger.LogInformation(
            "Validated package {PackageId} {PackageVersion}, persisting content to storage...",
            package.Id,
            package.NormalizedVersionString);

        RepositorySigningResult signing = Signing.Certificate == null
            ? RepositorySigningResult.Unsigned
            : await Signing.SignAsync(packageStream, Url.GetServiceIndexUrl(), cancellationToken);
        Stream storedPackageStream = signing.SignedPackage ?? packageStream;

        try
        {
            package.IsRepositorySigned = signing.IsRepositorySigned;

            // The catalog describes the package as stored, including any repository signature.
            storedPackageStream.Position = 0;
            (package.PackageHash, package.PackageSize) = await storedPackageStream.ComputeSha512Async(cancellationToken);
            storedPackageStream.Position = 0;

            await Storage.SavePackageContentAsync(
                package,
                storedPackageStream,
                nuspecStream,
                readmeStream,
                iconStream,
                licenseStream,
                cancellationToken);
        }
        catch (Exception e)
        {
            // This may happen due to concurrent pushes.
            // TODO: Make IPackageStorageService.SavePackageContentAsync return a result enum so this
            // can be properly handled.
            Logger.LogError(
                e,
                "Failed to persist package {PackageId} {PackageVersion} content to storage",
                package.Id,
                package.NormalizedVersionString);

            throw;
        }
        finally
        {
            signing.SignedPackage?.Dispose();
        }

        Logger.LogInformation(
            "Persisted package {Id} {Version} content to storage, saving metadata to database...",
            package.Id,
            package.NormalizedVersionString);

        PackageAddResult result = await Packages.AddAsync(package, cancellationToken);
        if (result == PackageAddResult.PackageAlreadyExists)
        {
            Logger.LogWarning(
                "Package {Id} {Version} metadata already exists in database",
                package.Id,
                package.NormalizedVersionString);

            return PackageIndexingResult.PackageAlreadyExists;
        }

        if (result != PackageAddResult.Success)
        {
            Logger.LogError($"Unknown {nameof(PackageAddResult)} value: {{PackageAddResult}}", result);

            throw new InvalidOperationException($"Unknown {nameof(PackageAddResult)} value: {result}");
        }

        await Catalog.AddPackageDetailsAsync([package], cancellationToken);

        Logger.LogInformation(
            "Successfully persisted package {Id} {Version} metadata to database. Indexing in search...",
            package.Id,
            package.NormalizedVersionString);

        await Search.IndexAsync(package, cancellationToken);

        Logger.LogInformation(
            "Successfully indexed package {Id} {Version} in search",
            package.Id,
            package.NormalizedVersionString);

        return PackageIndexingResult.Success;
    }
}
