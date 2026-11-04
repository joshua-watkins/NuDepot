using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

public class SearchContext
{
    public static SearchContext Default(string registrationBaseUrl)
    {
        return new SearchContext
        {
            Vocab = "http://schema.nuget.org/schema#",
            Base = registrationBaseUrl
        };
    }

    [JsonPropertyName("@vocab")]
    public string Vocab { get; set; } = null!;

    [JsonPropertyName("@base")]
    public string? Base { get; set; }
}
