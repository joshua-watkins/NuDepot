using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

/// <summary>
/// The JSON-LD context of an <see cref="AutocompleteResponse"/>.
/// </summary>
public class AutocompleteContext
{
    /// <summary>
    /// The context every autocomplete response uses.
    /// </summary>
    public static readonly AutocompleteContext Default = new()
    {
        Vocab = "http://schema.nuget.org/schema#"
    };

    /// <summary>
    /// The JSON-LD vocabulary, the NuGet schema.
    /// </summary>
    [JsonPropertyName("@vocab")]
    public string Vocab { get; set; } = null!;
}
