using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Storage;
using Xunit;

namespace NuDepot.Feed.Tests.Services
{
    public class CatalogServiceTests : IDisposable
    {
        // SQLite deletes an in-memory database when its last connection closes.
        private readonly SqliteConnection _keepAlive;
        private readonly NuDepotContext _context;
        private readonly CatalogService _target;

        public CatalogServiceTests()
        {
            var connectionString = $"Data Source=CatalogServiceTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

            _keepAlive = new SqliteConnection(connectionString);
            _keepAlive.Open();

            var options = new DbContextOptionsBuilder<NuDepotContext>()
                .UseSqlite(connectionString)
                .Options;

            _context = new NuDepotContext(options);
            _context.RunMigrationsAsync(default).GetAwaiter().GetResult();

            _target = new CatalogService(
                _context,
                Mock.Of<IUrlGenerator>(),
                Mock.Of<IPackageStorageService>(),
                new SystemTime(),
                NullLogger<CatalogService>.Instance);
        }

        public void Dispose()
        {
            _context.Dispose();
            _keepAlive.Dispose();
        }

        [Fact]
        public async Task PagesHoldMaxPageSizeItems()
        {
            for (var i = 0; i <= CatalogService.MaxPageSize; i++)
            {
                _context.CatalogEntries.Add(new CatalogEntry
                {
                    CommitId = Guid.NewGuid().ToString(),
                    CommittedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i),
                    Type = CatalogEntry.PackageDeleteType,
                    PackageId = "Package",
                    PackageVersion = $"1.0.{i}",
                });
            }

            await _context.SaveChangesAsync();

            var index = await _target.GetIndexAsync(default);
            var first = await _target.GetPageOrNullAsync(0, default);
            var second = await _target.GetPageOrNullAsync(1, default);

            Assert.Equal(2, index.Count);
            Assert.Equal(CatalogService.MaxPageSize, first!.Items.Count);
            Assert.Equal("1.0.0", first.Items.First().PackageVersion);
            Assert.Equal($"1.0.{CatalogService.MaxPageSize}", Assert.Single(second!.Items).PackageVersion);
            Assert.Null(await _target.GetPageOrNullAsync(2, default));
        }
    }
}
