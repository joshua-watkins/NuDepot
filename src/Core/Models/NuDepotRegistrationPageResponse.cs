using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

/// <summary>
/// A page of a package's registration that's its own document, because the package has too many versions to list
/// them all in its registration index. It's a <see cref="NuDepotRegistrationIndexPage"/> that links to its index.
///
/// See https://learn.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-page
/// </summary>
public class NuDepotRegistrationPageResponse : NuDepotRegistrationIndexPage
{
    /// <summary>
    /// The URL of the registration index the page belongs to.
    /// </summary>
    [JsonPropertyName("parent")]
    public string RegistrationIndexUrl { get; set; } = null!;
}
