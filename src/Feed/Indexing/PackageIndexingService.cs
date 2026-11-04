using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Packaging;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Search;
using NuDepot.Feed.Signing;
using NuDepot.Feed.Storage;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NuDepot.Feed.Indexing;

public class PackageIndexingService : IPackageIndexingService
{
    private readonly IPackageDatabase _packages;
    private readonly IPackageStorageService _storage;
    private readonly ISearchIndexer _search;
    private readonly ICatalogService _catalog;
    private readonly IRepositorySigningService _signing;
    private readonly IUrlGenerator _url;
    private readonly SystemTime _time;
    private readonly IOptionsSnapshot<FeedOptions> _options;
    private readonly ILogger<PackageIndexingService> _logger;

    public PackageIndexingService(
        IPackageDatabase packages,
        IPackageStorageService storage,
        ISearchIndexer search,
        ICatalogService catalog,
        IRepositorySigningService signing,
        IUrlGenerator url,
        SystemTime time,
        IOptionsSnapshot<FeedOptions> options,
        ILogger<PackageIndexingService> logger)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _signing = signing ?? throw new ArgumentNullException(nameof(signing));
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<PackageIndexingResult> IndexAsync(Stream packageStream, CancellationToken cancellationToken)
        => IndexAsync(packageStream, allowOverwrite: true, cancellationToken);

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
            using (var packageReader = new PackageArchiveReader(packageStream, leaveStreamOpen: true))
            {
                package = packageReader.GetPackageMetadata();
                package.PublishedAt = _time.UtcNow;

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
                    var license = await packageReader.GetLicenseAsync(cancellationToken)
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
            _logger.LogError(e, "Uploaded package is invalid");

            return PackageIndexingResult.InvalidPackage;
        }

        // The package is well-formed. Ensure this is a new package.
        if (await _packages.ExistsAsync(package.Id, package.Version, cancellationToken))
        {
            if (!_options.Value.AllowPackageOverwrites || !allowOverwrite)
            {
                return PackageIndexingResult.PackageAlreadyExists;
            }

            await _packages.HardDeletePackageAsync(package.Id, package.Version, cancellationToken);
            await _storage.DeleteAsync(package.Id, package.Version, cancellationToken);
            await _catalog.AddPackageDeleteAsync(package.Id, package.Version, cancellationToken);
        }

        // TODO: Add more package validations
        // TODO: Call PackageArchiveReader.ValidatePackageEntriesAsync
        _logger.LogInformation(
            "Validated package {PackageId} {PackageVersion}, persisting content to storage...",
            package.Id,
            package.NormalizedVersionString);

        var signing = _signing.Certificate == null
            ? RepositorySigningResult.Unsigned
            : await _signing.SignAsync(packageStream, _url.GetServiceIndexUrl(), cancellationToken);
        var storedPackageStream = signing.SignedPackage ?? packageStream;

        try
        {
            package.IsRepositorySigned = signing.IsRepositorySigned;

            // The catalog describes the package as stored, including any repository signature.
            storedPackageStream.Position = 0;
            (package.PackageHash, package.PackageSize) = await storedPackageStream.ComputeSha512Async(cancellationToken);
            storedPackageStream.Position = 0;

            await _storage.SavePackageContentAsync(
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
            _logger.LogError(
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

        _logger.LogInformation(
            "Persisted package {Id} {Version} content to storage, saving metadata to database...",
            package.Id,
            package.NormalizedVersionString);

        var result = await _packages.AddAsync(package, cancellationToken);
        if (result == PackageAddResult.PackageAlreadyExists)
        {
            _logger.LogWarning(
                "Package {Id} {Version} metadata already exists in database",
                package.Id,
                package.NormalizedVersionString);

            return PackageIndexingResult.PackageAlreadyExists;
        }

        if (result != PackageAddResult.Success)
        {
            _logger.LogError($"Unknown {nameof(PackageAddResult)} value: {{PackageAddResult}}", result);

            throw new InvalidOperationException($"Unknown {nameof(PackageAddResult)} value: {result}");
        }

        await _catalog.AddPackageDetailsAsync(new[] { package }, cancellationToken);

        _logger.LogInformation(
            "Successfully persisted package {Id} {Version} metadata to database. Indexing in search...",
            package.Id,
            package.NormalizedVersionString);

        await _search.IndexAsync(package, cancellationToken);

        _logger.LogInformation(
            "Successfully indexed package {Id} {Version} in search",
            package.Id,
            package.NormalizedVersionString);

        return PackageIndexingResult.Success;
    }
}
