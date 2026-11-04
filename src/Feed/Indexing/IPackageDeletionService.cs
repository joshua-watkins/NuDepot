using NuGet.Versioning;

namespace NuDepot.Feed.Indexing;

public interface IPackageDeletionService
{
    /// <summary>
    /// Attempt to delete a package.
    /// </summary>
    /// <param name="id">The id of the package to delete.</param>
    /// <param name="version">The version of the package to delete.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>False if the package does not exist.</returns>
    Task<bool> TryDeletePackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Attempt to relist a package that was unlisted.
    /// </summary>
    /// <returns>False if the package does not exist.</returns>
    Task<bool> TryRelistPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken);
}
