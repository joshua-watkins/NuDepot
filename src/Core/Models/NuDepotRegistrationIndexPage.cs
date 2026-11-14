using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

/// <summary>
/// NuDepot's extensions to a registration index page.
/// Extends <see cref="RegistrationIndexPage"/>.
/// </summary>
/// <remarks>
/// It doesn't inherit <see cref="RegistrationIndexPage"/>, because its <c>ItemsOrNull</c> property has the same JSON
/// name with a NuDepot type. Hiding the base class's property with <c>new</c> would leave that one always null for
/// code that uses the base class.
/// </remarks>
public class NuDepotRegistrationIndexPage
{
    #region Original properties from RegistrationIndexPage.
    /// <summary>
    /// The URL of this page.
    /// </summary>
    [JsonPropertyName("@id")]
    public string RegistrationPageUrl { get; set; } = null!;

    /// <summary>
    /// The number of package versions in this page.
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>
    /// The lowest version in this page.
    /// </summary>
    [JsonPropertyName("lower")]
    public string Lower { get; set; } = null!;

    /// <summary>
    /// The highest version in this page.
    /// </summary>
    [JsonPropertyName("upper")]
    public string Upper { get; set; } = null!;
    #endregion

    /// <summary>
    /// This was modified to use NuDepot's extended registration index page item model.
    /// </summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<NuDepotRegistrationIndexPageItem>? ItemsOrNull { get; set; }
}
