using NuGet.Versioning;

namespace NuDepot.Feed.Indexing;

/// <summary>
/// The result of attempting to index a package.
/// See <see cref="IPackageIndexingService.IndexAsync(Stream, CancellationToken)"/>.
/// </summary>
public enum PackageIndexingResult
{
    /// <summary>
    /// The package is malformed. This may also happen if NuDepot is in a corrupted state.
    /// </summary>
    InvalidPackage,

    /// <summary>
    /// The package has already been indexed.
    /// </summary>
    PackageAlreadyExists,

    /// <summary>
    /// The package has been indexed successfully.
    /// </summary>
    Success,
}

/// <summary>
/// The service used to accept new packages.
/// </summary>
public interface IPackageIndexingService
{
    /// <summary>
    /// Attempt to index a new package.
    /// </summary>
    /// <param name="stream">The stream containing the package's content.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The result of the attempted indexing operation.</returns>
    Task<PackageIndexingResult> IndexAsync(Stream stream, CancellationToken cancellationToken);

    /// <summary>
    /// Attempt to index a new package.
    /// </summary>
    /// <param name="stream">The stream containing the package's content.</param>
    /// <param name="allowOverwrite">
    /// Whether the package may replace an existing package, if the feed allows overwrites.
    /// If false, an existing package is <see cref="PackageIndexingResult.PackageAlreadyExists"/>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The result of the attempted indexing operation.</returns>
    Task<PackageIndexingResult> IndexAsync(Stream stream, bool allowOverwrite, CancellationToken cancellationToken);

    /// <summary>
    /// Index a package downloaded from a mirror, which never replaces a package the feed has.
    /// </summary>
    /// <param name="stream">The stream containing the package's content.</param>
    /// <param name="id">The package ID that was asked for.</param>
    /// <param name="version">The package version that was asked for.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>
    /// The result of the attempted indexing operation: <see cref="PackageIndexingResult.InvalidPackage"/> if the
    /// package isn't the one that was asked for.
    /// </returns>
    Task<PackageIndexingResult> MirrorAsync(Stream stream, string id, NuGetVersion version, CancellationToken cancellationToken);
}
