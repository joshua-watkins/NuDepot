using NuDepot.Core.Converters;
using System.Text.Json;
using Xunit;

namespace NuDepot.Core.Tests;

public class PackageDependencyRangeJsonConverterTests
{
    private static JsonSerializerOptions Options { get; } = new() { Converters = { new PackageDependencyRangeJsonConverter() } };

    [Fact]
    public void DeserializesNull()
    {
        string json = @"null";

        string? result = JsonSerializer.Deserialize<string>(json, Options);

        Assert.Null(result);
    }

    [Fact]
    public void DeserializesString()
    {
        string json = @"""Hello""";

        string? result = JsonSerializer.Deserialize<string>(json, Options);

        Assert.Equal("Hello", result);
    }

    [Fact]
    public void DeserializesStringArray()
    {
        string json = @"[""first"", ""second"", ""third""]";

        string? result = JsonSerializer.Deserialize<string>(json, Options);

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
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<string>(json, Options));
    }

    [Fact]
    public void SerializesNull()
    {
        string json = JsonSerializer.Serialize<string?>(null, Options);

        Assert.Equal("null", json);
    }

    [Fact]
    public void SerializesString()
    {
        string json = JsonSerializer.Serialize("foo", Options);

        Assert.Equal(@"""foo""", json);
    }
}
