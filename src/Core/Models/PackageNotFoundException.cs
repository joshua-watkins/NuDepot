using NuGet.Versioning;

namespace NuDepot.Core.Models;

/// <summary>
/// An exception thrown when a package could not be found on the NuGet server.
/// </summary>
/// <param name="packageId">The ID of the package that could not be found.</param>
/// <param name="packageVersion">The version of the package that could not be found.</param>
public class PackageNotFoundException(string packageId, NuGetVersion packageVersion) : Exception($"Could not find package {packageId} {packageVersion}")
{
    /// <summary>
    /// The package ID that could not be found.
    /// </summary>
    public string PackageId { get; } = packageId;

    /// <summary>
    /// The package version that could not be found.
    /// </summary>
    public NuGetVersion PackageVersion { get; } = packageVersion;
}
