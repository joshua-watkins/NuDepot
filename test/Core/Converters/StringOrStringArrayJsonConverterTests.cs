using NuDepot.Core.Converters;
using System.Text.Json;
using Xunit;

namespace NuDepot.Core.Tests;

public class StringOrStringArrayJsonConverterTests
{
    private static JsonSerializerOptions Options { get; } = new() { Converters = { new StringOrStringArrayJsonConverter() } };

    [Fact]
    public void DeserializesEmptyString()
    {
        string json = @"""""";

        IReadOnlyList<string>? result = JsonSerializer.Deserialize<IReadOnlyList<string>>(json, Options);

        string first = Assert.Single(result!);
        Assert.NotNull(first);
        Assert.True(string.IsNullOrEmpty(first));
    }

    [Fact]
    public void DeserializesString()
    {
        string json = @"""Foo bar""";

        IReadOnlyList<string>? result = JsonSerializer.Deserialize<IReadOnlyList<string>>(json, Options);

        string first = Assert.Single(result!);
        Assert.Equal("Foo bar", first);
    }

    [Fact]
    public void DeserializesEmptyArray()
    {
        string json = "[]";

        IReadOnlyList<string>? result = JsonSerializer.Deserialize<IReadOnlyList<string>>(json, Options);

        Assert.Empty(result!);
    }

    [Fact]
    public void DeserializesArray()
    {
        string json = @"[""Foo"", ""bar""]";

        IReadOnlyList<string>? result = JsonSerializer.Deserialize<IReadOnlyList<string>>(json, Options);

        Assert.Equal(2, result!.Count);
        Assert.Equal("Foo", result[0]);
        Assert.Equal("bar", result[1]);
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
            () => JsonSerializer.Deserialize<IReadOnlyList<string>>(json, Options));
    }

    [Fact]
    public void SerializesNull()
    {
        IReadOnlyList<string>? list = null;

        string json = JsonSerializer.Serialize(list, Options);

        Assert.Equal("null", json);
    }

    [Fact]
    public void SerializesEmptyString()
    {
        IReadOnlyList<string> list = [""];

        string json = JsonSerializer.Serialize(list, Options);

        Assert.Equal(@"[""""]", json);
    }

    [Fact]
    public void SerializesList()
    {
        IReadOnlyList<string> list = ["Hello", "World", null!];

        string json = JsonSerializer.Serialize(list, Options);

        Assert.Equal(@"[""Hello"",""World"",null]", json);
    }
}
