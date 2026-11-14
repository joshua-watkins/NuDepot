using Microsoft.Extensions.Logging.Abstractions;
using NuDepot.Core.Catalog;
using Xunit;

namespace NuDepot.Core.Tests.Catalog;

public sealed class FileCursorTests : IDisposable
{
    private string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cursor-{Guid.NewGuid():N}.json");
    private FileCursor Target { get; }

    public FileCursorTests()
    {
        Target = new FileCursor(NullLogger<FileCursor>.Instance, Path);
    }

    public void Dispose() => File.Delete(Path);

    [Fact]
    public async Task HasNoValueWithoutAFile()
    {
        Assert.Null(await Target.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadsTheValueItWrote()
    {
        DateTimeOffset value = new(2026, 11, 11, 18, 30, 15, TimeSpan.FromHours(-8));

        await Target.SetAsync(value, CancellationToken.None);
        await Target.SetAsync(value.AddMinutes(1), CancellationToken.None);

        Assert.Equal(value.AddMinutes(1), await Target.GetAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"value\": \"yesterday\"}")]
    [InlineData("{\"value\": 42}")]
    public async Task HasNoValueWhenTheFileIsNotACursor(string content)
    {
        await File.WriteAllTextAsync(Path, content);

        Assert.Null(await Target.GetAsync(CancellationToken.None));
    }
}
