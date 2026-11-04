using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace NuDepot.Feed.Tests;

public static class StreamExtensions
{
    public static string ToPrettifiedJson(this Stream jsonStream)
    {
        using JsonDocument document = JsonDocument.Parse(jsonStream);
        using MemoryStream output = new();
        using (Utf8JsonWriter writer = new(output, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            document.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }
}
