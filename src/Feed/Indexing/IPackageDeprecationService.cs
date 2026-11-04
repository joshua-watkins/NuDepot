using NuGet.Versioning;

namespace NuDepot.Feed.Indexing;

/// <summary>
/// Deprecates and undeprecates package versions.
/// </summary>
public interface IPackageDeprecationService
{
    /// <summary>
    /// Deprecate a package version, replacing any existing deprecation.
    /// </summary>
    /// <param name="id">The package's ID.</param>
    /// <param name="version">The package's version.</param>
    /// <param name="reasons">One or more of "Legacy", "CriticalBugs" or "Other".</param>
    /// <param name="message">An optional message for users.</param>
    /// <param name="alternatePackageId">An optional package to use instead.</param>
    /// <param name="alternatePackageVersionRange">The alternate package's versions. Defaults to any version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>False if the package doesn't exist.</returns>
    Task<bool> DeprecateAsync(string id, NuGetVersion version, IReadOnlyList<string> reasons, string? message, string? alternatePackageId, string? alternatePackageVersionRange, CancellationToken cancellationToken);

    /// <returns>False if the package doesn't exist.</returns>
    Task<bool> UndeprecateAsync(string id, NuGetVersion version, CancellationToken cancellationToken);
}
