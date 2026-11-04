using NuDepot.Core.Converters;
using System.Text.Json;
using Xunit;

namespace NuDepot.Core.Tests;

public class StringOrStringArrayJsonConverterTests
{
    [Fact]
    public void DeserializesEmptyString()
    {
        string json = @"""""";

        JsonSerializerOptions options = new();
        options.Converters.Add(new StringOrStringArrayJsonConverter());

        IReadOnlyList<string>? result = JsonSerializer.Deserialize<IReadOnlyList<string>>(json, options);

        string first = Assert.Single(result!);
        Assert.NotNull(first);
        Assert.True(string.IsNullOrEmpty(first));
    }

    [Fact]
    public void DeserializesString()
    {
        string json = @"""Foo bar""";

        JsonSerializerOptions options = new();
        options.Converters.Add(new StringOrStringArrayJsonConverter());

        IReadOnlyList<string>? result = JsonSerializer.Deserialize<IReadOnlyList<string>>(json, options);

        string first = Assert.Single(result!);
        Assert.Equal("Foo bar", first);
    }

    [Fact]
    public void DeserializesEmptyArray()
    {
        string json = "[]";

        JsonSerializerOptions options = new();
        options.Converters.Add(new StringOrStringArrayJsonConverter());

        IReadOnlyList<string>? result = JsonSerializer.Deserialize<IReadOnlyList<string>>(json, options);

        Assert.Empty(result!);
    }

    [Fact]
    public void DeserializesArray()
    {
        string json = @"[""Foo"", ""bar""]";

        JsonSerializerOptions options = new();
        options.Converters.Add(new StringOrStringArrayJsonConverter());

        IReadOnlyList<string>? result = JsonSerializer.Deserialize<IReadOnlyList<string>>(json, options);

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
        JsonSerializerOptions options = new();
        options.Converters.Add(new StringOrStringArrayJsonConverter());

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<IReadOnlyList<string>>(json, options));
    }

    [Fact]
    public void SerializesNull()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new StringOrStringArrayJsonConverter());

        IReadOnlyList<string>? list = null;

        string json = JsonSerializer.Serialize(list, options);

        Assert.Equal("null", json);
    }

    [Fact]
    public void SerializesEmptyString()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new StringOrStringArrayJsonConverter());

        IReadOnlyList<string> list = [""];

        string json = JsonSerializer.Serialize(list, options);

        Assert.Equal(@"[""""]", json);
    }

    [Fact]
    public void SerializesList()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new StringOrStringArrayJsonConverter());

        IReadOnlyList<string> list = ["Hello", "World", null!];

        string json = JsonSerializer.Serialize(list, options);

        Assert.Equal(@"[""Hello"",""World"",null]", json);
    }
}
