using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace NuDepot.Feed.Entities.Converters;

// Entity Framework doesn't convert nulls, so the conversions only see values.
public class UriToStringConverter : ValueConverter<Uri?, string?>
{
    public static readonly UriToStringConverter Instance = new();

    public UriToStringConverter()
        : base(
            v => v!.AbsoluteUri,
            v => new Uri(v!))
    {
    }
}
