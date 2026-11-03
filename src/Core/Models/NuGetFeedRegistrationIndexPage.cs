using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NuGet.Core.Models;

/// <summary>
/// NuGetFeed's extensions to a registration index page.
/// Extends <see cref="RegistrationIndexPage"/>.
/// </summary>
/// <remarks>
/// TODO: After this project is updated to .NET 5, make <see cref="NuGetFeedRegistrationIndexPage"/>
/// extend <see cref="RegistrationIndexPage"/> and remove identical properties.
/// Properties that are modified should be marked with the "new" modified.
/// See: https://github.com/dotnet/runtime/pull/32107
/// </remarks>
public class NuGetFeedRegistrationIndexPage
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
    /// This was modified to use NuGetFeed's extended registration index page item model.
    /// </summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<NuGetFeedRegistrationIndexPageItem>? ItemsOrNull { get; set; }
}
