using NuDepot.Feed.Catalog;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NuDepot.Feed.Serialization;

/// <summary>
/// Source generated JSON serialization for the types the server reads and writes outside of the NuGet protocol.
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(CatalogPackageSnapshot))]
internal partial class ServerJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Serialization for values stored in the database. This only escapes what JSON requires,
    /// so stored values read the same as they did when the server used Json.NET.
    /// </summary>
    public static ServerJsonContext Database { get; } = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}
