using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace NuDepot.Core.Catalog;

/// <summary>
/// A cursor implementation which stores the cursor in local file. The cursor value is written to the file as
/// a JSON object.
/// Based off: https://github.com/NuGet/NuGet.Services.Metadata/blob/3a468fe534a03dcced897eb5992209fdd3c4b6c9/src/NuGet.Protocol.Catalog/FileCursor.cs
/// </summary>
public partial class FileCursor(string path, ILogger<FileCursor> logger) : ICursor
{
    private const string ValueProperty = "value";

    private string Path { get; } = path;
    private ILogger<FileCursor> Logger { get; } = logger;

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using FileStream file = File.OpenRead(Path);
            using JsonDocument document = await JsonDocument.ParseAsync(file, cancellationToken: cancellationToken);
            DateTimeOffset value = document.RootElement
                .GetProperty(ValueProperty)
                .GetDateTimeOffset();
            Log.Read(Logger, value, Path);
            return value;
        }
        catch (Exception e) when (e is FileNotFoundException || e is JsonException || e is KeyNotFoundException || e is FormatException || e is InvalidOperationException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task SetAsync(DateTimeOffset value, CancellationToken cancellationToken)
    {
        using (FileStream file = File.Create(Path))
        using (Utf8JsonWriter writer = new(file))
        {
            writer.WriteStartObject();
            writer.WriteString(ValueProperty, value);
            writer.WriteEndObject();
        }

        Log.Wrote(Logger, value, Path);
        return Task.CompletedTask;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Read cursor value {cursor:O} from {path}.")]
        public static partial void Read(ILogger logger, DateTimeOffset cursor, string path);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Wrote cursor value {cursor:O} to {path}.")]
        public static partial void Wrote(ILogger logger, DateTimeOffset cursor, string path);
    }
}
