using NuDepot.Feed.Indexing;
using Xunit;

namespace NuDepot.Feed.Tests.Services;

/// <summary>
/// Tests which packages' frameworks a search's framework can use.
/// </summary>
public class FrameworkCompatibilityServiceTests
{
    private FrameworkCompatibilityService Target { get; } = new();

    [Theory]
    [InlineData("net8.0", new[] { "any", "net8.0", "net6.0", "net5.0", "netcoreapp3.1", "netstandard2.1", "netstandard2.0", "netstandard1.0" }, new[] { "net9.0", "net472" })]
    [InlineData("netstandard2.0", new[] { "any", "netstandard2.0", "netstandard1.6", "netstandard1.0" }, new[] { "netstandard2.1", "net8.0" })]
    [InlineData("net472", new[] { "any", "net472", "net471", "net45", "netstandard2.0" }, new[] { "net48", "netstandard2.1", "net8.0" })]
    public void FindsFrameworksAFrameworkCanUse(string framework, string[] compatible, string[] incompatible)
    {
        IReadOnlyList<string> frameworks = Target.FindAllCompatibleFrameworks(framework);

        Assert.All(compatible, f => Assert.Contains(f, frameworks));
        Assert.All(incompatible, f => Assert.DoesNotContain(f, frameworks));
    }

    [Fact]
    public void UnknownFrameworksOnlyMatchThemselvesAndAny()
    {
        Assert.Equal(["tizen40", "any"], Target.FindAllCompatibleFrameworks("tizen40"));
    }

    [Fact]
    public void RemembersEachFrameworksAnswer()
    {
        Assert.Same(Target.FindAllCompatibleFrameworks("net8.0"), new FrameworkCompatibilityService().FindAllCompatibleFrameworks("net8.0"));
    }
}
