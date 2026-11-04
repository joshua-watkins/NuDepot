namespace NuDepot.Feed.Entities;

// See NuGetGallery.Core's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/PackageDependency.cs
/// <summary>
/// A package version's dependency on another package, for a target framework.
/// </summary>
public class PackageDependency
{
    /// <summary>
    /// The row's key.
    /// </summary>
    public int Key { get; set; }

    /// <summary>
    /// The dependency's package ID. Null if this is a dependency group without any dependencies.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// The dependency's package version. Null if this is a dependency group without any dependencies.
    /// </summary>
    public string? VersionRange { get; set; }

    /// <summary>
    /// The dependency group's target framework. Null for dependencies on any framework.
    /// </summary>
    public string? TargetFramework { get; set; }

    /// <summary>
    /// The package version with the dependency.
    /// </summary>
    public Package? Package { get; set; }
}
