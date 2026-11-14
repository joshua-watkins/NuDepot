using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

/// <summary>
/// NuDepot's extensions to a registration index page item.
/// Extends <see cref="RegistrationIndexPageItem"/>.
/// </summary>
/// <remarks>
/// It doesn't inherit <see cref="RegistrationIndexPageItem"/>, because its <c>PackageMetadata</c> property has the same JSON
/// name with a NuDepot type. Hiding the base class's property with <c>new</c> would leave that one always null for
/// code that uses the base class.
/// </remarks>
public class NuDepotRegistrationIndexPageItem
{
    #region Original properties from RegistrationIndexPageItem.
    /// <summary>
    /// The URL of this version's registration leaf.
    /// </summary>
    [JsonPropertyName("@id")]
    public string RegistrationLeafUrl { get; set; } = null!;

    /// <summary>
    /// The URL to download this version's package.
    /// </summary>
    [JsonPropertyName("packageContent")]
    public string PackageContentUrl { get; set; } = null!;
    #endregion

    /// <summary>
    /// The catalog entry containing the package metadata.
    /// This was modified to use NuDepot's extended package metadata model.
    /// </summary>
    [JsonPropertyName("catalogEntry")]
    public NuDepotPackageMetadata PackageMetadata { get; set; } = null!;
}
