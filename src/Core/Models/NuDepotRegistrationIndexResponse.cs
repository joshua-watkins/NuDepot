using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

/// <summary>
/// NuDepot's extensions to a registration index response.
/// Extends <see cref="RegistrationIndexResponse"/>.
/// </summary>
/// <remarks>
/// It doesn't inherit <see cref="RegistrationIndexResponse"/>, because its <c>Pages</c> property has the same JSON
/// name with a NuDepot type. Hiding the base class's property with <c>new</c> would leave that one always null for
/// code that uses the base class.
/// </remarks>
public class NuDepotRegistrationIndexResponse
{
    #region Original properties from RegistrationIndexResponse.
    /// <summary>
    /// The URL of this registration index.
    /// </summary>
    [JsonPropertyName("@id")]
    public string RegistrationIndexUrl { get; set; } = null!;

    /// <summary>
    /// The JSON-LD types of this registration index.
    /// </summary>
    [JsonPropertyName("@type")]
    public IReadOnlyList<string> Type { get; set; } = null!;

    /// <summary>
    /// The number of pages in this registration index.
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }
    #endregion

    /// <summary>
    /// The pages that contain all of the versions of the package, ordered
    /// by the package's version. This was modified to use NuDepot's extended
    /// registration index page model.
    /// </summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<NuDepotRegistrationIndexPage> Pages { get; set; } = null!;

    /// <summary>
    /// The package's total downloads across all versions.
    /// This is not part of the official NuGet protocol.
    /// </summary>
    [JsonPropertyName("totalDownloads")]
    public long TotalDownloads { get; set; }
}
