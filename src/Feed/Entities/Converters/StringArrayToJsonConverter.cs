using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NuDepot.Feed.Serialization;
using System.Text.Json;

namespace NuDepot.Feed.Entities.Converters;

// Entity Framework doesn't convert nulls, so the conversions only see values.
/// <summary>
/// Stores a string array in one column, as a JSON array.
/// </summary>
public class StringArrayToJsonConverter : ValueConverter<string[]?, string?>
{
    /// <summary>
    /// The converter.
    /// </summary>
    public static readonly StringArrayToJsonConverter Instance = new();

    /// <summary>
    /// Creates the converter. Use <see cref="Instance"/>.
    /// </summary>
    public StringArrayToJsonConverter()
        : base(
            v => JsonSerializer.Serialize(v, ServerJsonContext.Database.StringArray),
            v => !string.IsNullOrEmpty(v) ? JsonSerializer.Deserialize(v, ServerJsonContext.Database.StringArray) : new string[0])
    {
    }
}
