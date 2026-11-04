using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Search;
using NuDepot.Feed.Signing;
using NuDepot.Feed.Storage;
using Xunit;

namespace NuDepot.Feed.Tests.Services;

public class PackageIndexingServiceTests
{
    private Mock<IPackageDatabase> Packages { get; }
    private Mock<IPackageStorageService> Storage { get; }
    private Mock<ISearchIndexer> Search { get; }
    private Mock<SystemTime> Time { get; }
    private PackageIndexingService Target { get; }

    public PackageIndexingServiceTests()
    {
        Packages = new Mock<IPackageDatabase>();
        Storage = new Mock<IPackageStorageService>();
        Search = new Mock<ISearchIndexer>();
        Time = new Mock<SystemTime>();

        Target = new PackageIndexingService(
            Packages.Object,
            Storage.Object,
            Search.Object,
            Mock.Of<ICatalogService>(),
            Mock.Of<IRepositorySigningService>(),
            Mock.Of<IUrlGenerator>(),
            Time.Object,
            Mock.Of<IOptionsSnapshot<FeedOptions>>(),
            Mock.Of<ILogger<PackageIndexingService>>());
    }

    // TODO: Add malformed package tests

    [Fact]
    public async Task WhenPackageAlreadyExists_ReturnsPackageAlreadyExists()
    {
        await Task.Yield();
    }

    [Fact]
    public async Task WhenDatabaseAddFailsBecausePackageAlreadyExists_ReturnsPackageAlreadyExists()
    {
        await Task.Yield();
    }

    [Fact]
    public async Task IndexesPackage()
    {
        await Task.Yield();
    }

    [Fact]
    public async Task WhenPackageHasNoReadme_SavesNullReadmeStream()
    {
        await Task.Yield();
    }

    [Fact]
    public async Task ThrowsWhenStorageSaveThrows()
    {
        await Task.Yield();
    }
}
