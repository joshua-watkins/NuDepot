using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

/// <summary>
/// The JSON-LD context of a <see cref="SearchResponse"/>.
/// </summary>
public class SearchContext
{
    /// <summary>
    /// Creates the context of a search response.
    /// </summary>
    /// <param name="registrationBaseUrl">The base URL of the feed's package metadata resource.</param>
    /// <returns>The context.</returns>
    public static SearchContext Default(string registrationBaseUrl)
    {
        return new SearchContext
        {
            Vocab = "http://schema.nuget.org/schema#",
            Base = registrationBaseUrl
        };
    }

    /// <summary>
    /// The JSON-LD vocabulary, the NuGet schema.
    /// </summary>
    [JsonPropertyName("@vocab")]
    public string Vocab { get; set; } = null!;

    /// <summary>
    /// The base URL that relative URLs in the response resolve against.
    /// </summary>
    [JsonPropertyName("@base")]
    public string? Base { get; set; }
}
