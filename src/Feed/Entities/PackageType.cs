namespace NuDepot.Feed.Entities;

// See NuGetGallery.Core's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/PackageType.cs
/// <summary>
/// A package version's type, like "Dependency" or "DotnetTool".
/// </summary>
public class PackageType
{
    /// <summary>
    /// The row's key.
    /// </summary>
    public int Key { get; set; }

    /// <summary>
    /// The type's name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The type's version, if the package set one.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// The package version with the type.
    /// </summary>
    public Package Package { get; set; } = null!;
}
