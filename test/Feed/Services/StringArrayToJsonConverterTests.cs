using NuDepot.Feed.Entities.Converters;
using Xunit;

namespace NuDepot.Feed.Tests.Services;

public class StringArrayToJsonConverterTests
{
    private StringArrayToJsonConverter Converter { get; } = StringArrayToJsonConverter.Instance;

    [Fact]
    public void StoresValuesLikeJsonNet()
    {
        string? stored = (string?)Converter.ConvertToProvider((string[])["c++", "<web>", "café", "a\"b"]);

        Assert.Equal("[\"c++\",\"<web>\",\"café\",\"a\\\"b\"]", stored);
    }

    [Theory]
    [InlineData("[\"json\",\"c++\"]", new[] { "json", "c++" })]
    [InlineData("[\"caf\\u00e9\"]", new[] { "café" })]
    [InlineData("[]", new string[0])]
    [InlineData("", new string[0])]
    public void ReadsStoredValues(string stored, string[] expected)
    {
        Assert.Equal(expected, (string[]?)Converter.ConvertFromProvider(stored));
    }
}
