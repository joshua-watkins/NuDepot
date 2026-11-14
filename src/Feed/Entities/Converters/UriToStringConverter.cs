using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace NuDepot.Feed.Entities.Converters;

// Entity Framework doesn't convert nulls, so the conversions only see values.
/// <summary>
/// Stores a URI as its string.
/// </summary>
internal class UriToStringConverter : ValueConverter<Uri?, string?>
{
    /// <summary>
    /// The converter.
    /// </summary>
    public static readonly UriToStringConverter Instance = new();

    /// <summary>
    /// Creates the converter. Use <see cref="Instance"/>.
    /// </summary>
    public UriToStringConverter()
        : base(
            v => v!.AbsoluteUri,
            v => new Uri(v!))
    {
    }
}
