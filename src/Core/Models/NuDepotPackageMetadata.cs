using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

/// <summary>
/// NuDepot's extensions to the package metadata model. These additions
/// are not part of the official protocol.
/// </summary>
public class NuDepotPackageMetadata : Models.PackageMetadata
{
    /// <summary>
    /// The total downloads of this version.
    /// </summary>
    [JsonPropertyName("downloads")]
    public long Downloads { get; set; }

    /// <summary>
    /// Whether the package has a README.
    /// </summary>
    [JsonPropertyName("hasReadme")]
    public bool HasReadme { get; set; }

    /// <summary>
    /// The package's types, like "Dependency" or "DotnetTool".
    /// </summary>
    [JsonPropertyName("packageTypes")]
    public IReadOnlyList<string>? PackageTypes { get; set; }

    /// <summary>
    /// The package's release notes.
    /// </summary>
    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; set; }

    /// <summary>
    /// The URL of the package's source repository.
    /// </summary>
    [JsonPropertyName("repositoryUrl")]
    public string? RepositoryUrl { get; set; }

    /// <summary>
    /// The type of the package's source repository, like "git".
    /// </summary>
    [JsonPropertyName("repositoryType")]
    public string? RepositoryType { get; set; }
}
