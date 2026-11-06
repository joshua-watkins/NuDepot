using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NuDepot.Core.Models;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Storage;
using Xunit;

namespace NuDepot.Feed.Tests.Services;

public class CatalogServiceTests : IDisposable
{
    // SQLite deletes an in-memory database when its last connection closes.
    private SqliteConnection KeepAlive { get; }
    private NuDepotContext Context { get; }
    private CatalogService Target { get; }

    public CatalogServiceTests()
    {
        string connectionString = $"Data Source=CatalogServiceTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        KeepAlive = new SqliteConnection(connectionString);
        KeepAlive.Open();

        DbContextOptions<NuDepotContext> options = new DbContextOptionsBuilder<NuDepotContext>()
            .UseSqlite(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        Context = new NuDepotContext(options);
        Context
            .RunMigrationsAsync(default)
            .GetAwaiter()
            .GetResult();

        Target = new CatalogService(
            Context,
            Mock.Of<IUrlGenerator>(),
            Mock.Of<IPackageStorageService>(),
            new SystemTime(),
            NullLogger<CatalogService>.Instance);
    }

    public void Dispose()
    {
        Context.Dispose();
        KeepAlive.Dispose();
    }

    [Fact]
    public async Task PagesHoldMaxPageSizeItems()
    {
        await Context.CatalogEntries.ExecuteInsertAsync(
            Enumerable
                .Range(0, CatalogService.MaxPageSize + 1)
                .Select(i => new CatalogEntry
                {
                    CommitId = Guid.NewGuid().ToString(),
                    CommittedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i),
                    Type = CatalogEntry.PackageDeleteType,
                    PackageId = "Package",
                    PackageVersion = $"1.0.{i}",
                }),
            default);

        CatalogIndex index = await Target.GetIndexAsync(default);
        CatalogPage? first = await Target.GetPageOrNullAsync(0, default);
        CatalogPage? second = await Target.GetPageOrNullAsync(1, default);

        Assert.Equal(2, index.Count);
        Assert.Equal(CatalogService.MaxPageSize, first!.Items.Count);
        Assert.Equal("1.0.0", first.Items.First().PackageVersion);
        Assert.Equal($"1.0.{CatalogService.MaxPageSize}", Assert.Single(second!.Items).PackageVersion);
        Assert.Null(await Target.GetPageOrNullAsync(2, default));
    }
}
