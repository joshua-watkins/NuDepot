using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

public class AutocompleteContext
{
    public static readonly AutocompleteContext Default = new()
    {
        Vocab = "http://schema.nuget.org/schema#"
    };

    [JsonPropertyName("@vocab")]
    public string Vocab { get; set; } = null!;
}
