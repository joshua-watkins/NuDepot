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
    public async Task ImportsTheFoldersPackagesAndSymbols()
    {
        Console.SetOut(Out);
        using NuDepotTestApplication app = new(output);
        string folder = CreateFolder(new()
        {
            ["import.flat.1.0.0.nupkg"] = TestPackages.Create("Import.Flat", "1.0.0"),
            [Path.Combine("import.nested", "2.0.0", "import.nested.2.0.0.nupkg")] = TestPackages.Create("Import.Nested", "2.0.0"),
            [Path.Combine("build", TestResources.Package)] = TestResources.GetResourceStream(TestResources.Package),
            [Path.Combine("build", TestResources.SymbolPackage)] = TestResources.GetResourceStream(TestResources.SymbolPackage),
            ["old.1.0.0.symbols.nupkg"] = TestPackages.Create("Old", "1.0.0"),
            ["broken.nupkg"] = new MemoryStream("not a package"u8.ToArray()),
        });

        Assert.True(await app.Host.TryRunCommandAsync(["import", "packages", folder]));

        // The package that isn't valid fails the command, but not the others.
        string imported = Out.ToString();
        Assert.Equal(1, Environment.ExitCode);
        Assert.Contains("Added Import.Flat 1.0.0", imported);
        Assert.Contains("Added Import.Nested 2.0.0", imported);
        Assert.Contains("Added TestData 1.2.3", imported);
        Assert.Contains($"Added symbols {Path.Combine("build", TestResources.SymbolPackage)}", imported);
        Assert.Contains("Not a valid package: broken.nupkg", imported);
        Assert.Contains("Packages: 3 added, 0 already there, 1 failed.", imported);
        Assert.Contains("Ignored 1 old-style .symbols.nupkg packages", imported);
        Assert.Equal(["Import.Flat", "Import.Nested", "TestData"], await PackageIdsAsync(app, mirrored: false));

        // Importing again skips them.
        Out.GetStringBuilder().Clear();
        Assert.True(await app.Host.TryRunCommandAsync(["import", "packages", folder]));
        Assert.Contains("Packages: 0 added, 3 already there, 1 failed.", Out.ToString());
    }

    [Fact]
    public async Task ImportsMirroredPackages()
    {
        Console.SetOut(Out);
        using NuDepotTestApplication app = new(output);
        string folder = CreateFolder(new() { ["mirrored.1.0.0.nupkg"] = TestPackages.Create("Mirrored", "1.0.0") });

        Assert.True(await app.Host.TryRunCommandAsync(["import", "packages", "--mirrored", folder]));

        Assert.Equal(0, Environment.ExitCode);
        Assert.Equal(["Mirrored"], await PackageIdsAsync(app, mirrored: true));
    }

    [Theory]
    [InlineData(null, "Give the folder to import")]
    [InlineData("packages", "Give the folder's full path, rather than 'packages'")]
    [InlineData("missing", "doesn't exist, or NuDepot can't read it")]
    public async Task ImportNeedsAFolder(string? folder, string error)
    {
        Console.SetError(Error);
        using NuDepotTestApplication app = new(output);
        if (folder == "missing")
            folder = Path.Combine(Path.GetTempPath(), "NuDepotTests", Guid.NewGuid().ToString("N"));

        Assert.True(await app.Host.TryRunCommandAsync(folder == null ? ["import", "packages"] : ["import", "packages", folder]));

        Assert.Equal(1, Environment.ExitCode);
        Assert.Contains(error, Error.ToString());
    }

    [Fact]
    public async Task ImportsIntoAFeedThatSignsWithItsAddress()
    {
        Console.SetOut(Out);
        Console.SetError(Error);
        using NuDepotTestApplication app = new(
            output,
            configuration: new Dictionary<string, string> { ["RepositorySigning:IsEnabled"] = "true" },
            configureServices: services => services.AddSingleton(new Signing.GeneratedCertificateKeySize(2048)));
        string folder = CreateFolder(new() { ["signed.1.0.0.nupkg"] = TestPackages.Create("Signed", "1.0.0") });

        // The signatures record the feed's address, which only requests have.
        Assert.True(await app.Host.TryRunCommandAsync(["import", "packages", folder]));
        Assert.Equal(1, Environment.ExitCode);
        Assert.Contains("Give it with --service-index", Error.ToString());

        Environment.ExitCode = 0;
        Assert.True(await app.Host.TryRunCommandAsync(["import", "packages", folder, "--service-index", "https://nuget.example.com/v3/index.json"]));
        Assert.Equal(0, Environment.ExitCode);

        using IServiceScope scope = app.Services.CreateScope();
        Assert.True(await scope.ServiceProvider
            .GetRequiredService<NuDepotDbContext>()
            .Packages
            .Select(p => p.IsRepositorySigned)
            .SingleAsync(TestContext.Current.CancellationToken));
    }

    // A folder with the files, in a temporary folder.
    private static string CreateFolder(Dictionary<string, Stream> files)
    {
        string folder = Path.Combine(Path.GetTempPath(), "NuDepotTests", Guid.NewGuid().ToString("N"), "import");
        foreach ((string name, Stream content) in files)
        {
            string path = Path.Combine(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (content)
            using (FileStream file = File.Create(path))
                content.CopyTo(file);
        }

        return folder;
    }

    private static async Task<List<string>> PackageIdsAsync(NuDepotTestApplication app, bool mirrored)
    {
        using IServiceScope scope = app.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<NuDepotDbContext>()
            .Packages
            .Where(p => p.IsMirrored == mirrored)
            .Select(p => p.Id)
            .OrderBy(id => id)
            .ToListAsync(TestContext.Current.CancellationToken);
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
