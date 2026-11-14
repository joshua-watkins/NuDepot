using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

/// <summary>
/// A package that depends on the queried package.
/// </summary>
public class PackageDependent
{
    /// <summary>
    /// The dependent package id.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    /// <summary>
    /// The description of the dependent package.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// The total downloads for the dependent package.
    /// </summary>
    [JsonPropertyName("totalDownloads")]
    public long TotalDownloads { get; set; }
}
