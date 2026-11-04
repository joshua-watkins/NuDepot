using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

/// <summary>
/// NuDepot's extensions to a registration index page.
/// Extends <see cref="RegistrationIndexPage"/>.
/// </summary>
/// <remarks>
/// TODO: After this project is updated to .NET 5, make <see cref="NuDepotRegistrationIndexPage"/>
/// extend <see cref="RegistrationIndexPage"/> and remove identical properties.
/// Properties that are modified should be marked with the "new" modified.
/// See: https://github.com/dotnet/runtime/pull/32107
/// </remarks>
public class NuDepotRegistrationIndexPage
{
#region Original properties from RegistrationIndexPage.
    [JsonPropertyName("@id")]
    public string RegistrationPageUrl { get; set; } = null!;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("lower")]
    public string Lower { get; set; } = null!;

    [JsonPropertyName("upper")]
    public string Upper { get; set; } = null!;
#endregion

    /// <summary>
    /// This was modified to use NuDepot's extended registration index page item model.
    /// </summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<NuDepotRegistrationIndexPageItem>? ItemsOrNull { get; set; }
}
