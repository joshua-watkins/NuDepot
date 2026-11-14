using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Tests.Support;
using NuDepot.Feed.Upstream;
using Xunit;

namespace NuDepot.Feed.Tests;

[Collection(nameof(ConsoleCollection))]
public sealed class NuDepotCommandsTests(ITestOutputHelper output) : IDisposable
{
    private TextWriter OriginalOut { get; } = Console.Out;
    private TextWriter OriginalError { get; } = Console.Error;
    private StringWriter Out { get; } = new();
    private StringWriter Error { get; } = new();

    public void Dispose()
    {
        Console.SetOut(OriginalOut);
        Console.SetError(OriginalError);
        Environment.ExitCode = 0;
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void WritesHelp(string option)
    {
        Console.SetOut(Out);

        Assert.True(NuDepotCommands.TryWriteHelp(["import", option]));

        Assert.Contains("import downloads", Out.ToString());
        Assert.Contains("rotate signing-certificate", Out.ToString());
    }

    [Fact]
    public void OtherArgumentsArentHelp()
    {
        Assert.False(NuDepotCommands.TryWriteHelp(["--urls", "http://localhost:5000"]));
    }

    [Theory]
    [InlineData]
    [InlineData("--environment", "Development")]
    [InlineData("--Feed:Database:ConnectionString=Data Source=nudepot.db")]
    [InlineData("--urls", "http://localhost:5000", "/contentRoot", "folder")]
    public async Task OptionsAloneRunTheServer(params string[] args)
    {
        using NuDepotTestApplication app = new(output);

        Assert.False(await app.Host.TryRunCommandAsync(args));
        Assert.Equal(0, Environment.ExitCode);
    }

    [Fact]
    public async Task UnknownCommandFails()
    {
        Console.SetError(Error);
        using NuDepotTestApplication app = new(output);

        Assert.True(await app.Host.TryRunCommandAsync(["--environment", "Development", "import", "everything"]));

        Assert.Equal(1, Environment.ExitCode);
        Assert.Contains("Unknown command 'import everything'", Error.ToString());
    }

    [Fact]
    public async Task ImportsDownloads()
    {
        FakeDownloads downloads = new()
        {
            Counts = new() { ["testdata"] = new() { ["1.2.3"] = 1234, ["9.9.9"] = 1 }, ["other"] = new() { ["1.0.0"] = 5 } },
        };
        using NuDepotTestApplication app = new(output, configureServices: services => services.AddSingleton<IPackageDownloadsSource>(downloads));
        await app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await app.Host.TryRunCommandAsync(["import", "downloads"]));

        using IServiceScope scope = app.Services.CreateScope();
        Package package = await scope.ServiceProvider
            .GetRequiredService<NuDepotDbContext>()
            .Packages
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1234, package.Downloads);
        Assert.Equal(0, Environment.ExitCode);
    }

    [Fact]
    public async Task ImportKeepsDownloadsOfPackagesTheSourceDoesntHave()
    {
        FakeDownloads downloads = new() { Counts = [] };
        using NuDepotTestApplication app = new(output, configureServices: services => services.AddSingleton<IPackageDownloadsSource>(downloads));
        await app.AddPackageAsync(TestResources.GetResourceStream(TestResources.Package), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await app.Host.TryRunCommandAsync(["import", "downloads"]));

        using IServiceScope scope = app.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<NuDepotDbContext>().Packages.SumAsync(p => p.Downloads, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RotatingNeedsAGeneratedCertificate()
    {
        Console.SetError(Error);
        using NuDepotTestApplication app = new(output);

        Assert.True(await app.Host.TryRunCommandAsync(["rotate", "signing-certificate"]));

        Assert.Equal(1, Environment.ExitCode);
        Assert.Contains("Only generated certificates can be rotated", Error.ToString());
    }

    private sealed class FakeDownloads : IPackageDownloadsSource
    {
        public required Dictionary<string, Dictionary<string, long>> Counts { get; init; }

        public Task<Dictionary<string, Dictionary<string, long>>> GetPackageDownloadsAsync() => Task.FromResult(Counts);
    }
}
