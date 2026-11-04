using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace NuDepot.Feed.Tests
{
    public static class StreamExtensions
    {
        public static string ToPrettifiedJson(this Stream jsonStream)
        {
            using var document = JsonDocument.Parse(jsonStream);
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions
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
}
