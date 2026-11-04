using NuDepot.Core.Converters;
using System.Text.Json;
using Xunit;

namespace NuDepot.Core.Tests;

public class PackageDependencyRangeJsonConverterTests
{
    [Fact]
    public void DeserializesNull()
    {
        string json = @"null";

        JsonSerializerOptions options = new();
        options.Converters.Add(new PackageDependencyRangeJsonConverter());

        string? result = JsonSerializer.Deserialize<string>(json, options);

        Assert.Null(result);
    }

    [Fact]
    public void DeserializesString()
    {
        string json = @"""Hello""";

        JsonSerializerOptions options = new();
        options.Converters.Add(new PackageDependencyRangeJsonConverter());

        string? result = JsonSerializer.Deserialize<string>(json, options);

        Assert.Equal("Hello", result);
    }

    [Fact]
    public void DeserializesStringArray()
    {
        string json = @"[""first"", ""second"", ""third""]";

        JsonSerializerOptions options = new();
        options.Converters.Add(new PackageDependencyRangeJsonConverter());

        string? result = JsonSerializer.Deserialize<string>(json, options);

        Assert.Equal("first", result);
    }

    [Theory]
    [InlineData(@"false")]
    [InlineData(@"0")]
    [InlineData(@"{")]
    [InlineData(@"[")]
    [InlineData(@"[""hello""")]
    [InlineData(@"[""hello""}")]
    [InlineData(@"[""hello"", 1")]
    public void ThrowsOnInvalidJson(string json)
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new PackageDependencyRangeJsonConverter());

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<string>(json, options));
    }

    [Fact]
    public void SerializesNull()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new StringOrStringArrayJsonConverter());

        string json = JsonSerializer.Serialize<string?>(null, options);

        Assert.Equal("null", json);
    }

    [Fact]
    public void SerializesString()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new StringOrStringArrayJsonConverter());

        string json = JsonSerializer.Serialize("foo", options);

        Assert.Equal(@"""foo""", json);
    }
}
