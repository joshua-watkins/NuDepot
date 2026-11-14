using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Storage;
using NuGet.Versioning;
using Xunit;

namespace NuDepot.Feed.Tests.Services;

public class PackageDeletionServiceTests
{
    private static readonly string PackageId = "Package";
    private static readonly NuGetVersion PackageVersion = new("1.0.0");

    private Mock<IPackageDatabase> Packages { get; }
    private Mock<IPackageStorageService> Storage { get; }
    private PackageLocks Locks { get; } = new();

    private FeedOptions Options { get; }
    private PackageDeletionService Target { get; }

    public PackageDeletionServiceTests()
    {
        Packages = new Mock<IPackageDatabase>();
        Storage = new Mock<IPackageStorageService>();
        Options = new FeedOptions();

        Mock<IOptionsSnapshot<FeedOptions>> optionsSnapshot = new();
        optionsSnapshot
            .Setup(o => o.Value)
            .Returns(Options);

        Target = new PackageDeletionService(
            NullLogger<PackageDeletionService>.Instance,
            Packages.Object,
            Storage.Object,
            Mock.Of<ICatalogService>(),
            Locks,
            optionsSnapshot.Object,
            new HttpContextAccessor());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenUnlist_ReturnsTrueOnlyIfPackageExists(bool packageExists)
    {
        // Arrange
        CancellationToken cancellationToken = CancellationToken.None;
        Options.PackageDeletionBehavior = PackageDeletionBehavior.Unlist;

        Packages
            .Setup(p => p.UnlistPackageAsync(PackageId, PackageVersion, cancellationToken))
            .ReturnsAsync(packageExists);

        // Act
        bool result = await Target.TryDeletePackageAsync(PackageId, PackageVersion, cancellationToken);

        // Assert
        Assert.Equal(packageExists, result);

        Packages.Verify(
            p => p.UnlistPackageAsync(PackageId, PackageVersion, cancellationToken),
            Times.Once);

        Packages.Verify(
            p => p.HardDeletePackageAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Storage.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenHardDelete_ReturnsTrueOnlyIfPackageExists(bool packageExists)
    {
        // Arrange
        Options.PackageDeletionBehavior = PackageDeletionBehavior.HardDelete;

        int step = 0;
        int databaseStep = -1;
        int storageStep = -1;
        CancellationToken cancellationToken = CancellationToken.None;

        Packages
            .Setup(p => p.HardDeletePackageAsync(PackageId, PackageVersion, cancellationToken))
            .Callback(() => databaseStep = step++)
            .ReturnsAsync(packageExists);

        Storage
            .Setup(s => s.DeleteAsync(PackageId, PackageVersion, cancellationToken))
            .Callback(() => storageStep = step++)
            .Returns(Task.CompletedTask);

        // Act
        bool result = await Target.TryDeletePackageAsync(PackageId, PackageVersion, cancellationToken);

        // Assert - The database step MUST happen before the storage step.
        Assert.Equal(packageExists, result);
        Assert.Equal(0, databaseStep);
        Assert.Equal(1, storageStep);

        // The storage deletion should happen even if the package couldn't
        // be found in the database. This ensures consistency.
        Packages.Verify(
            p => p.HardDeletePackageAsync(PackageId, PackageVersion, cancellationToken),
            Times.Once);
        Storage.Verify(
            s => s.DeleteAsync(PackageId, PackageVersion, cancellationToken),
            Times.Once);

        Packages.Verify(
            p => p.UnlistPackageAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HardDeleteWaitsForAPushOfTheVersion()
    {
        Options.PackageDeletionBehavior = PackageDeletionBehavior.HardDelete;
        Packages
            .Setup(p => p.HardDeletePackageAsync(PackageId, PackageVersion, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // A push of the version holds its lock.
        SemaphoreSlim packageLock = Locks.For(PackageId, PackageVersion);
        await packageLock.WaitAsync(TestContext.Current.CancellationToken);

        Task<bool> deletion = Target.TryDeletePackageAsync(PackageId, PackageVersion, CancellationToken.None);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.False(deletion.IsCompleted);
        Packages.Verify(
            p => p.HardDeletePackageAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()),
            Times.Never);

        packageLock.Release();

        Assert.True(await deletion);
        Assert.Equal(1, packageLock.CurrentCount);
    }
}
