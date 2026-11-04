using NuDepot.Core.Models;
using NuGet.Versioning;

namespace NuDepot.Core.PackageDetails;

/// <summary>
/// The client used to fetch the information shown on a package's details page.
/// This builds on the Package Metadata resource, NuDepot's extensions to it,
/// and the README URI template resource.
///
/// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
/// See https://learn.microsoft.com/en-us/nuget/api/readme-template-resource
/// </summary>
public interface IPackageDetailsClient
{
    /// <summary>
    /// Find the metadata for all versions of a package, including unlisted versions.
    /// Servers that don't implement NuDepot's extensions to the protocol return
    /// default values for the extended properties.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package's metadata, or an empty list if the package does not exist.</returns>
    Task<IReadOnlyList<NuDepotPackageMetadata>> GetPackageMetadataAsync(string packageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Download a package's readme, or null if the package has no readme or the
    /// server does not have a README URI template resource.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="packageVersion">The package's version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The readme stream, or null. The stream may not be seekable.</returns>
    Task<Stream?> GetPackageReadmeStreamOrNullAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find the packages that depend on a given package. Returns no results if the
    /// server does not have a package dependents resource.
    /// This is an unofficial API that isn't part of the NuGet protocol.
    /// </summary>
    /// <param name="packageId">The package whose dependents should be found.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The dependents response.</returns>
    Task<DependentsResponse> GetPackageDependentsAsync(string packageId, CancellationToken cancellationToken = default);
}
