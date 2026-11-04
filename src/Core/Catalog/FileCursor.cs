using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NuDepot.Core.Catalog;

/// <summary>
/// A cursor implementation which stores the cursor in local file. The cursor value is written to the file as
/// a JSON object.
/// Based off: https://github.com/NuGet/NuGet.Services.Metadata/blob/3a468fe534a03dcced897eb5992209fdd3c4b6c9/src/NuGet.Protocol.Catalog/FileCursor.cs
/// </summary>
public class FileCursor : ICursor
{
    private const string ValueProperty = "value";

    private readonly string _path;
    private readonly ILogger<FileCursor> _logger;

    public FileCursor(string path, ILogger<FileCursor> logger)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DateTimeOffset?> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using (var file = File.OpenRead(_path))
            using (var document = await JsonDocument.ParseAsync(file, cancellationToken: cancellationToken))
            {
                var value = document.RootElement.GetProperty(ValueProperty).GetDateTimeOffset();
                _logger.LogDebug("Read cursor value {cursor:O} from {path}.", value, _path);
                return value;
            }
        }
        catch (Exception e) when (e is FileNotFoundException || e is JsonException || e is KeyNotFoundException || e is FormatException || e is InvalidOperationException)
        {
            return null;
        }
    }

    public Task SetAsync(DateTimeOffset value, CancellationToken cancellationToken)
    {
        using (var file = File.Create(_path))
        using (var writer = new Utf8JsonWriter(file))
        {
            writer.WriteStartObject();
            writer.WriteString(ValueProperty, value);
            writer.WriteEndObject();
        }

        _logger.LogDebug("Wrote cursor value {cursor:O} to {path}.", value, _path);
        return Task.CompletedTask;
    }
}
