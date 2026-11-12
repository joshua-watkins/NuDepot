using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Search;
using NuDepot.Feed.Signing;
using NuDepot.Feed.Storage;
using NuGet.Versioning;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace NuDepot.Feed.Tests.Services;

public class PackageIndexingServiceTests : IDisposable
{
    private static readonly NuGetVersion Version = NuGetVersion.Parse("1.2.3");

    private Mock<IPackageDatabase> Packages { get; } = new();
    private Mock<IPackageStorageService> Storage { get; } = new();
    private Mock<ISearchIndexer> Search { get; } = new();
    private Mock<ICatalogService> Catalog { get; } = new();
    private Mock<IRepositorySigningService> Signing { get; } = new();
    private FeedOptions Options { get; } = new();
    private PackageLocks Locks { get; } = new();
    private PackageIndexingService Target { get; }

    private CancellationToken Cancellation { get; } = CancellationToken.None;

    public PackageIndexingServiceTests()
    {
        Mock<IOptionsSnapshot<FeedOptions>> options = new();
        options
            .Setup(o => o.Value)
            .Returns(Options);

        Target = CreateTarget(options.Object);

        Packages
            .Setup(p => p.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackageAddResult.Success);
    }

    public void Dispose() => Locks.Dispose();

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    public async Task RejectsStreamsThatArentPackages(byte[] content)
    {
        using MemoryStream stream = new(content);

        Assert.Equal(PackageIndexingResult.InvalidPackage, await Target.IndexAsync(stream, Cancellation));

        Storage.VerifyNoOtherCalls();
        Packages.Verify(p => p.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("lib/../../evil.dll")]
    public async Task RejectsPackagesWithEntriesOutsideTheirFolder(string entry)
    {
        using MemoryStream stream = CreatePackage(entry);

        Assert.Equal(PackageIndexingResult.InvalidPackage, await Target.IndexAsync(stream, Cancellation));

        Storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AcceptsAPackageWithSafeEntries()
    {
        using MemoryStream stream = CreatePackage("lib/net10.0/Safe.dll");

        Assert.Equal(PackageIndexingResult.Success, await Target.IndexAsync(stream, Cancellation));
    }

    [Fact]
    public async Task IndexesPackage()
    {
        using MemoryStream stream = TestResources.GetResourceStream(TestResources.Package);

        Assert.Equal(PackageIndexingResult.Success, await Target.IndexAsync(stream, Cancellation));

        Storage.Verify(s => s.SavePackageContentAsync(
            It.Is<Package>(p => p.Id == "TestData" && p.PackageHash != null),
            stream,
            It.IsAny<Stream>(),
            It.IsAny<Stream?>(),
            It.IsAny<Stream?>(),
            It.IsAny<Stream?>(),
            It.IsAny<CancellationToken>()));
        Packages.Verify(p => p.AddAsync(It.Is<Package>(p => p.Id == "TestData"), It.IsAny<CancellationToken>()));
        Catalog.Verify(c => c.AddPackageDetailsAsync(It.IsAny<IReadOnlyList<Package>>(), It.IsAny<CancellationToken>()));
        Search.Verify(s => s.IndexAsync(It.Is<Package>(p => p.Id == "TestData"), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task PushedPackagesArentMirrored()
    {
        using MemoryStream stream = TestResources.GetResourceStream(TestResources.Package);

        await Target.IndexAsync(stream, Cancellation);

        Packages.Verify(p => p.AddAsync(It.Is<Package>(p => !p.IsMirrored), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task MirrorsThePackageThatWasAskedFor()
    {
        using MemoryStream stream = TestResources.GetResourceStream(TestResources.Package);

        Assert.Equal(PackageIndexingResult.Success, await Target.MirrorAsync(stream, "testdata", NuGetVersion.Parse("1.2.3.0"), Cancellation));

        Packages.Verify(p => p.AddAsync(It.Is<Package>(p => p.Id == "TestData" && p.IsMirrored), It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData("Other.Package", "1.2.3")]
    [InlineData("TestData", "1.2.4")]
    public async Task MirrorsOnlyThePackageThatWasAskedFor(string id, string version)
    {
        using MemoryStream stream = TestResources.GetResourceStream(TestResources.Package);

        Assert.Equal(PackageIndexingResult.InvalidPackage, await Target.MirrorAsync(stream, id, NuGetVersion.Parse(version), Cancellation));

        Storage.VerifyNoOtherCalls();
        Packages.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MirroringNeverReplacesAPackage()
    {
        Options.AllowPackageOverwrites = true;
        Packages
            .Setup(p => p.ExistsAsync("TestData", Version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using MemoryStream stream = TestResources.GetResourceStream(TestResources.Package);

        Assert.Equal(PackageIndexingResult.PackageAlreadyExists, await Target.MirrorAsync(stream, "TestData", Version, Cancellation));
        Packages.Verify(p => p.HardDeletePackageAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeletesFilesLeftByAFailedPushBeforeStoring()
    {
        List<string> calls = [];
        Storage
            .Setup(s => s.DeleteAsync("TestData", Version, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("delete"));
        Storage
            .Setup(s => s.SavePackageContentAsync(It.IsAny<Package>(), It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<Stream?>(), It.IsAny<Stream?>(), It.IsAny<Stream?>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("save"));

        using MemoryStream stream = TestResources.GetResourceStream(TestResources.Package);
        await Target.IndexAsync(stream, Cancellation);

        Assert.Equal(["delete", "save"], calls);
    }

    [Fact]
    public async Task WhenPackageAlreadyExists_ReturnsPackageAlreadyExists()
    {
        Packages
            .Setup(p => p.ExistsAsync("TestData", Version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        using MemoryStream stream = TestResources.GetResourceStream(TestResources.Package);

        Assert.Equal(PackageIndexingResult.PackageAlreadyExists, await Target.IndexAsync(stream, Cancellation));

        Storage.VerifyNoOtherCalls();
        Packages.Verify(p => p.HardDeletePackageAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task OnlyOverwritesWhenTheFeedAndPusherAllowIt(bool feedAllows, bool pusherAllows)
    {
        Options.AllowPackageOverwrites = feedAllows;
        Packages
            .Setup(p => p.ExistsAsync("TestData", Version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        using MemoryStream stream = TestResources.GetResourceStream(TestResources.Package);

        Assert.Equal(PackageIndexingResult.PackageAlreadyExists, await Target.IndexAsync(stream, pusherAllows, Cancellation));
    }

    [Fact]
    public async Task OverwritesPackage()
    {
        Options.AllowPackageOverwrites = true;
        Packages
            .Setup(p => p.ExistsAsync("TestData", Version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        using MemoryStream stream = TestResources.GetResourceStream(TestResources.Package);

        Assert.Equal(PackageIndexingResult.Success, await Target.IndexAsync(stream, allowOverwrite: true, Cancellation));

        Packages.Verify(p => p.HardDeletePackageAsync("TestData", Version, It.IsAny<CancellationToken>()));
        Catalog.Verify(c => c.AddPackageDeleteAsync("TestData", Version, It.IsAny<CancellationToken>()));
        Packages.Verify(p => p.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task WhenDatabaseAddFailsBecausePackageAlreadyExists_ReturnsPackageAlreadyExists()
    {
        Packages
            .Setup(p => p.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PackageAddResult.PackageAlreadyExists);

        using MemoryStream stream = TestResources.GetResourceStream(TestResources.Package);

        Assert.Equal(PackageIndexingResult.PackageAlreadyExists, await Target.IndexAsync(stream, Cancellation));

        Search.Verify(s => s.IndexAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WhenStorageSaveThrows_DeletesWhatWasStored()
    {
        Storage
            .Setup(s => s.SavePackageContentAsync(It.IsAny<Package>(), It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<Stream?>(), It.IsAny<Stream?>(), It.IsAny<Stream?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("The disk is full."));

        using MemoryStream stream = TestResources.GetResourceStream(TestResources.Package);

        await Assert.ThrowsAsync<IOException>(() => Target.IndexAsync(stream, Cancellation));

        // Once before storing, and once after failing.
        Storage.Verify(s => s.DeleteAsync("TestData", Version, It.IsAny<CancellationToken>()), Times.Exactly(2));
        Packages.Verify(p => p.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WhenDatabaseAddThrows_DeletesWhatWasStored()
    {
        Packages
            .Setup(p => p.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The database is locked."));

        using MemoryStream stream = TestResources.GetResourceStream(TestResources.Package);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Target.IndexAsync(stream, Cancellation));

        Storage.Verify(s => s.DeleteAsync("TestData", Version, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ConcurrentPushesOfTheSameVersionAddItOnce()
    {
        // The database records the package a while after it's asked, like a slow disk.
        int added = 0;
        Packages
            .Setup(p => p.ExistsAsync("TestData", Version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Volatile.Read(ref added) > 0);
        Packages
            .Setup(p => p.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(100);
                Interlocked.Increment(ref added);
                return PackageAddResult.Success;
            });

        using MemoryStream first = TestResources.GetResourceStream(TestResources.Package);
        using MemoryStream second = TestResources.GetResourceStream(TestResources.Package);

        PackageIndexingResult[] results = await Task.WhenAll(
            Task.Run(() => Target.IndexAsync(first, allowOverwrite: false, Cancellation)),
            Task.Run(() => Target.IndexAsync(second, allowOverwrite: false, Cancellation)));

        Assert.Equal(1, added);
        Assert.Contains(PackageIndexingResult.Success, results);
        Assert.Contains(PackageIndexingResult.PackageAlreadyExists, results);
    }

    [Fact]
    public async Task DisposesTheFilesItCopiedFromThePackage()
    {
        Stream? nuspec = null;
        Storage
            .Setup(s => s.SavePackageContentAsync(It.IsAny<Package>(), It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<Stream?>(), It.IsAny<Stream?>(), It.IsAny<Stream?>(), It.IsAny<CancellationToken>()))
            .Callback((Package _, Stream _, Stream copied, Stream? _, Stream? _, Stream? _, CancellationToken _) => nuspec = copied);

        using MemoryStream stream = TestResources.GetResourceStream(TestResources.Package);
        await Target.IndexAsync(stream, Cancellation);

        FileStream file = Assert.IsType<FileStream>(nuspec);
        Assert.False(file.CanRead);
        Assert.False(File.Exists(file.Name));
    }

    private PackageIndexingService CreateTarget(IOptionsSnapshot<FeedOptions> options)
    {
        Signing
            .Setup(s => s.GetRepositorySignatureAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RepositorySignatureDetails?)null);

        return new PackageIndexingService(
            Packages.Object,
            Storage.Object,
            Search.Object,
            Catalog.Object,
            Signing.Object,
            Mock.Of<IUrlGenerator>(),
            new SystemTime(),
            Locks,
            options,
            Mock.Of<ILogger<PackageIndexingService>>());
    }

    // A package with a nuspec and one other entry.
    private static MemoryStream CreatePackage(string entry)
    {
        MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream nuspec = archive.CreateEntry("Unsafe.nuspec").Open())
            {
                nuspec.Write(Encoding.UTF8.GetBytes("""
                    <?xml version="1.0" encoding="utf-8"?>
                    <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                      <metadata>
                        <id>Unsafe</id>
                        <version>1.0.0</version>
                        <authors>Contoso</authors>
                        <description>A package.</description>
                      </metadata>
                    </package>
                    """));
            }

            using (Stream content = archive.CreateEntry(entry).Open())
            {
                content.Write([1, 2, 3]);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
