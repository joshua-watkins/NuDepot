using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Storage;
using NuDepot.Feed.Tests.Support;
using NuGet.Protocol;
using NuGet.Versioning;
using System.Security.Cryptography;
using Xunit;

namespace NuDepot.Feed.Tests;

/// <summary>
/// The packages folder is laid out like a NuGet folder feed, so NuGet clients can read it as a package source.
/// </summary>
public sealed class PackageFolderFeedTests(ITestOutputHelper output) : IDisposable
{
    private NuDepotTestApplication App { get; } = new(output);

    private string PackagesFolder => Path.Combine(App.Services.GetRequiredService<IOptions<FeedOptions>>().Value.Storage.Path!, "packages");

    public void Dispose() => App.Dispose();

    [Fact]
    public async Task NuGetReadsPushedPackagesFromThePackagesFolder()
    {
        await App.AddPackageAsync(TestPackages.Create("Folder.Feed", "1.0.0-Beta"), cancellationToken: TestContext.Current.CancellationToken);

        LocalPackageInfo? package = LocalFolderUtility.GetPackageV3(PackagesFolder, "Folder.Feed", NuGetVersion.Parse("1.0.0-beta"), NuGet.Common.NullLogger.Instance);

        Assert.NotNull(package);
        Assert.Equal(HashOf(package.Path), await File.ReadAllTextAsync(package.Path + ".sha512", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeletedPackagesLeaveThePackagesFolder()
    {
        await App.AddPackageAsync(TestPackages.Create("Folder.Feed", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);

        // The test feed deletes packages, rather than unlisting them.
        using (IServiceScope scope = App.Services.CreateScope())
        {
            Assert.True(await scope.ServiceProvider
                .GetRequiredService<IPackageDeletionService>()
                .TryDeletePackageAsync("Folder.Feed", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken));
        }

        Assert.Null(LocalFolderUtility.GetPackageV3(PackagesFolder, "Folder.Feed", NuGetVersion.Parse("1.0.0"), NuGet.Common.NullLogger.Instance));
        Assert.False(Directory.EnumerateFiles(Path.Combine(PackagesFolder, "folder.feed", "1.0.0")).Any());
    }

    [Fact]
    public async Task PackagesStoredBeforeTheHashFilesGetThemAtStartup()
    {
        await App.AddPackageAsync(TestPackages.Create("Hashed.Package", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);
        await App.AddPackageAsync(TestPackages.Create("Unhashed.Package", "1.0.0"), cancellationToken: TestContext.Current.CancellationToken);

        // Like packages from before the feed wrote hash files, and one from before it recorded hashes.
        foreach (string hashFile in Directory.EnumerateFiles(PackagesFolder, "*.sha512", SearchOption.AllDirectories))
            File.Delete(hashFile);
        using (IServiceScope scope = App.Services.CreateScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<NuDepotDbContext>()
                .Packages
                .Where(p => p.Id == "Unhashed.Package")
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.PackageHash, (string?)null), TestContext.Current.CancellationToken);
        }

        Assert.Null(LocalFolderUtility.GetPackageV3(PackagesFolder, "Hashed.Package", NuGetVersion.Parse("1.0.0"), NuGet.Common.NullLogger.Instance));

        MissingHashFilesWriter writer = new(NullLogger<MissingHashFilesWriter>.Instance, App.Services.GetRequiredService<IServiceScopeFactory>());
        await writer.StartAsync(TestContext.Current.CancellationToken);
        await writer.ExecuteTask!;

        foreach (string id in (string[])["Hashed.Package", "Unhashed.Package"])
        {
            LocalPackageInfo? package = LocalFolderUtility.GetPackageV3(PackagesFolder, id, NuGetVersion.Parse("1.0.0"), NuGet.Common.NullLogger.Instance);
            Assert.NotNull(package);
            Assert.Equal(HashOf(package.Path), await File.ReadAllTextAsync(package.Path + ".sha512", TestContext.Current.CancellationToken));
        }
    }

    private static string HashOf(string file) => Convert.ToBase64String(SHA512.HashData(File.ReadAllBytes(file)));
}
