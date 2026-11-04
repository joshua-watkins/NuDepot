using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NuDepot.Feed.Serialization;

namespace NuDepot.Feed.Entities.Converters;

// Entity Framework doesn't convert nulls, so the conversions only see values.
public class StringArrayToJsonConverter : ValueConverter<string[]?, string?>
{
    public static readonly StringArrayToJsonConverter Instance = new StringArrayToJsonConverter();

    public StringArrayToJsonConverter()
        : base(
            v => JsonSerializer.Serialize(v, ServerJsonContext.Database.StringArray),
            v => !string.IsNullOrEmpty(v) ? JsonSerializer.Deserialize(v, ServerJsonContext.Database.StringArray) : new string[0])
    {
    }
}
