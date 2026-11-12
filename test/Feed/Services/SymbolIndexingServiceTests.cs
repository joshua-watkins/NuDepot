using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Storage;
using NuGet.Versioning;
using System.IO.Compression;
using Xunit;

namespace NuDepot.Feed.Tests.Services;

public class SymbolIndexingServiceTests
{
    private Mock<IPackageDatabase> Packages { get; } = new();
    private Mock<ISymbolStorageService> Storage { get; } = new();
    private SymbolIndexingService Target { get; }

    public SymbolIndexingServiceTests()
    {
        Packages
            .Setup(p => p.FindOrNullAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Package());
        Storage
            .Setup(s => s.SavePortablePdbContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Target = new SymbolIndexingService(Packages.Object, Storage.Object, NullLogger<SymbolIndexingService>.Instance);
    }

    [Fact]
    public async Task SavesPdbs()
    {
        using MemoryStream snupkg = SymbolPackage();

        Assert.Equal(SymbolIndexingResult.Success, await Target.IndexAsync(snupkg, CancellationToken.None));

        Storage.Verify(s => s.SavePortablePdbContentAsync(
            It.Is<string>(f => f.EndsWith(".pdb", StringComparison.Ordinal)),
            It.Is<string>(k => k.Length == 40 && k.EndsWith("ffffffff", StringComparison.Ordinal)),
            It.IsAny<Stream>(),
            It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData("../outside.pdb")]
    [InlineData("readme.txt")]
    public async Task RejectsUnsafeOrUnexpectedEntries(string entry)
    {
        using MemoryStream snupkg = SymbolPackage((entry, [1, 2, 3]));

        Assert.Equal(SymbolIndexingResult.InvalidSymbolPackage, await Target.IndexAsync(snupkg, CancellationToken.None));
        Storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StoresNothingIfAnyPdbIsntPortable()
    {
        using MemoryStream snupkg = SymbolPackage(("lib/net8.0/zzz.broken.pdb", [1, 2, 3]));

        Assert.Equal(SymbolIndexingResult.InvalidSymbolPackage, await Target.IndexAsync(snupkg, CancellationToken.None));
        Storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RejectsStreamsThatArentSymbolPackages()
    {
        using MemoryStream snupkg = new([1, 2, 3]);

        Assert.Equal(SymbolIndexingResult.InvalidSymbolPackage, await Target.IndexAsync(snupkg, CancellationToken.None));
    }

    [Fact]
    public async Task NeedsThePackage()
    {
        Packages
            .Setup(p => p.FindOrNullAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Package?)null);
        using MemoryStream snupkg = SymbolPackage();

        Assert.Equal(SymbolIndexingResult.PackageNotFound, await Target.IndexAsync(snupkg, CancellationToken.None));
        Storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ADifferentPdbWithTheSameKeyConflicts()
    {
        Storage
            .Setup(s => s.SavePortablePdbContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        using MemoryStream snupkg = SymbolPackage();

        Assert.Equal(SymbolIndexingResult.SymbolsConflict, await Target.IndexAsync(snupkg, CancellationToken.None));
    }

    [Fact]
    public async Task StorageFailuresArentTheSymbolPackagesFault()
    {
        Storage
            .Setup(s => s.SavePortablePdbContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("The disk is full."));
        using MemoryStream snupkg = SymbolPackage();

        await Assert.ThrowsAsync<IOException>(() => Target.IndexAsync(snupkg, CancellationToken.None));
    }

    [Fact]
    public async Task CancellationIsntAnInvalidSymbolPackage()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        using MemoryStream snupkg = SymbolPackage();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Target.IndexAsync(snupkg, cancellation.Token));
    }

    // The test symbol package, with extra entries.
    private static MemoryStream SymbolPackage(params (string Name, byte[] Content)[] entries)
    {
        MemoryStream stream = new();
        using (MemoryStream resource = TestResources.GetResourceStream(TestResources.SymbolPackage))
            resource.CopyTo(stream);

        using (ZipArchive archive = new(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in entries)
            {
                using Stream entry = archive.CreateEntry(name).Open();
                entry.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
