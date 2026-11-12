using NuDepot.Core.Models;
using NuGet.Versioning;

namespace NuDepot.Feed.Metadata;

/// <summary>
/// The Package Metadata client, used to fetch packages' metadata.
/// 
/// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
/// </summary>
public interface IPackageMetadataService
{
    /// <summary>
    /// Attempt to get a package's registration index, if it exists.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-page
    /// </summary>
    /// <param name="packageId">The package's ID.</param>
    /// <param name="semVer2">Whether to include SemVer 2.0.0 packages (RegistrationsBaseUrl/3.6.0).</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package's registration index, or null if the package does not exist</returns>
    Task<NuDepotRegistrationIndexResponse?> GetRegistrationIndexOrNullAsync(string packageId, bool semVer2 = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempt to get a page of a package's registration, which its index links to when the package has too many
    /// versions to list in the index.
    /// See: https://learn.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-page
    /// </summary>
    /// <param name="packageId">The package's ID.</param>
    /// <param name="lower">The page's lowest version.</param>
    /// <param name="upper">The page's highest version.</param>
    /// <param name="semVer2">Whether to include SemVer 2.0.0 packages (RegistrationsBaseUrl/3.6.0).</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The page, or null if the package's index doesn't link to a page with those versions.</returns>
    Task<NuDepotRegistrationPageResponse?> GetRegistrationPageOrNullAsync(string packageId, NuGetVersion lower, NuGetVersion upper, bool semVer2 = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the metadata for a single package version, if the package exists.
    /// </summary>
    /// <param name="packageId">The package's id.</param>
    /// <param name="packageVersion">The package's version.</param>
    /// <param name="semVer2">Whether to include SemVer 2.0.0 packages (RegistrationsBaseUrl/3.6.0).</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The registration leaf, or null if the package does not exist.</returns>
    Task<RegistrationLeafResponse?> GetRegistrationLeafOrNullAsync(string packageId, NuGetVersion packageVersion, bool semVer2 = false, CancellationToken cancellationToken = default);
}
