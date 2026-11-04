using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

/// <summary>
/// The metadata for a single version of a package.
/// 
/// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-leaf
/// </summary>
public class RegistrationLeafResponse
{
    public static readonly IReadOnlyList<string> DefaultType =
    [
        "Package",
        "http://schema.nuget.org/catalog#Permalink"
    ];

    /// <summary>
    /// The URL to the registration leaf.
    /// </summary>
    [JsonPropertyName("@id")]
    public string RegistrationLeafUrl { get; set; } = null!;

    /// <summary>
    /// The registration leaf's type.
    /// </summary>
    [JsonPropertyName("@type")]
    public IReadOnlyList<string> Type { get; set; } = null!;

    /// <summary>
    /// Whether the package is listed.
    /// </summary>
    [JsonPropertyName("listed")]
    public bool Listed { get; set; }

    /// <summary>
    /// The URL to the package content (.nupkg).
    /// </summary>
    [JsonPropertyName("packageContent")]
    public string PackageContentUrl { get; set; } = null!;

    /// <summary>
    /// The date the package was published. On NuGet.org, <see cref="Published"/>
    /// is set to the year 1900 if the package is unlisted.
    /// </summary>
    [JsonPropertyName("published")]
    public DateTimeOffset Published { get; set; }

    /// <summary>
    /// The URL to the package's registration index.
    /// </summary>
    [JsonPropertyName("registration")]
    public string RegistrationIndexUrl { get; set; } = null!;

    /// <summary>
    /// The URL to the package's latest catalog leaf, if the server has a catalog.
    /// </summary>
    [JsonPropertyName("catalogEntry")]
    public string? CatalogEntryUrl { get; set; }
}
