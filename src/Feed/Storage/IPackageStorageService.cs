using NuDepot.Feed.Entities;
using NuGet.Versioning;

namespace NuDepot.Feed.Storage;

/// <summary>
/// Stores packages' content. Packages' state are stored by the
/// <see cref="IPackageDatabase"/>.
/// </summary>
public interface IPackageStorageService
{
    /// <summary>
    /// Persist a package's content to storage. This operation MUST fail if a package
    /// with the same id/version but different content has already been stored.
    /// </summary>
    /// <param name="package">The package's metadata.</param>
    /// <param name="packageStream">The package's nupkg stream.</param>
    /// <param name="nuspecStream">The package's nuspec stream.</param>
    /// <param name="readmeStream">The package's readme stream, or null if none.</param>
    /// <param name="iconStream">The package's icon stream, or null if none.</param>
    /// <param name="licenseStream">The package's license stream, or null if none</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>A task that completes when the content is stored.</returns>
    Task SavePackageContentAsync(Package package, Stream packageStream, Stream nuspecStream, Stream? readmeStream, Stream? iconStream, Stream? licenseStream, CancellationToken cancellationToken);

    /// <summary>
    /// Replace a package's nupkg, like when it's re-signed. Its other files stay the same.
    /// </summary>
    /// <param name="id">The package's id.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="packageStream">The package's new nupkg stream.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>A task that completes when the new nupkg is stored.</returns>
    Task ReplacePackageStreamAsync(string id, NuGetVersion version, Stream packageStream, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieve a package's nupkg stream.
    /// </summary>
    /// <param name="id">The package's id.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package's nupkg stream.</returns>
    Task<Stream?> GetPackageStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieve a package's nuspec stream.
    /// </summary>
    /// <param name="id">The package's id.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package's nuspec stream.</returns>
    Task<Stream?> GetNuspecStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieve a package's readme stream.
    /// </summary>
    /// <param name="id">The package's id.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package's readme stream.</returns>
    Task<Stream?> GetReadmeStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieve a package's icon stream.
    /// </summary>
    /// <param name="id">The package's id.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package's icon stream.</returns>
    Task<Stream?> GetIconStreamAsync(string id, NuGetVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the packages license stream if the license is an embedded file.
    /// </summary>
    /// <param name="id">The package's id.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="licenseIsMarkDown">Whether the license file is Markdown, rather than text.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package's license stream, or null if it doesn't have a license file.</returns>
    Task<Stream?> GetLicenseStreamAsync(string id, NuGetVersion version, bool licenseIsMarkDown, CancellationToken cancellationToken);

    /// <summary>
    /// Remove a package's content from storage. This operation SHOULD succeed
    /// even if the package does not exist.
    /// </summary>
    /// <param name="id">The package's id.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>A task that completes when the content is removed.</returns>
    Task DeleteAsync(string id, NuGetVersion version, CancellationToken cancellationToken);
}
