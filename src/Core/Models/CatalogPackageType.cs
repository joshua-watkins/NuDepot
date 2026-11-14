using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

/// <summary>
/// A package type in a <see cref="PackageDetailsCatalogLeaf"/>.
/// </summary>
public class CatalogPackageType
{
    /// <summary>
    /// The package type's name, like "Dependency" or "DotnetTool".
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Only present if the author explicitly specified a version in the nuspec.
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
