using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

/// <summary>
/// The metadata for a package and all of its versions.
/// 
/// See https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-index
/// </summary>
public class RegistrationIndexResponse
{
    public static readonly IReadOnlyList<string> DefaultType =
    [
        "catalog:CatalogRoot",
        "PackageRegistration",
        "catalog:Permalink"
    ];

    /// <summary>
    /// The URL to the registration index.
    /// </summary>
    [JsonPropertyName("@id")]
    public string RegistrationIndexUrl { get; set; } = null!;

    /// <summary>
    /// The registration index's type.
    /// </summary>
    [JsonPropertyName("@type")]
    public IReadOnlyList<string> Type { get; set; } = null!;

    /// <summary>
    /// The number of registration pages. See <see cref="Pages"/>.
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>
    /// The pages that contain all of the versions of the package, ordered
    /// by the package's version.
    /// </summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<RegistrationIndexPage> Pages { get; set; } = null!;
}
