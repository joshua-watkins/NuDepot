using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using NuDepot.Core.Models;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Search;
using NuDepot.Feed.Vulnerabilities;
using NuGet.Versioning;
using Xunit;

namespace NuDepot.Feed.Tests.Services;

public class DatabaseSearchServiceTests
{
    public class FindDependentsAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsEachDependentOnceWithItsTotalDownloads()
        {
            await AddAsync(CreatePackage("A", "1.0.0", downloads: 5, "A one", dependsOn: "Target"));
            await AddAsync(CreatePackage("A", "2.0.0", downloads: 7, "A two", dependsOn: "Target"));
            await AddAsync(CreatePackage("B", "1.0.0", downloads: 10, "B one", dependsOn: "Target"));
            await AddAsync(CreatePackage("Other", "1.0.0", downloads: 100, "Other", dependsOn: "Unrelated"));

            DependentsResponse response = await _target.FindDependentsAsync("Target", TestContext.Current.CancellationToken);

            Assert.Equal(2, response.TotalHits);
            Assert.Collection(
                response.Data,
                a =>
                {
                    Assert.Equal("A", a.Id);
                    Assert.Equal(12, a.TotalDownloads);
                    Assert.Equal("A two", a.Description);
                },
                b =>
                {
                    Assert.Equal("B", b.Id);
                    Assert.Equal(10, b.TotalDownloads);
                });
        }

        [Fact]
        public async Task CountsEveryVersionsDownloadsButDescribesTheLatestListedVersion()
        {
            // Only 1.0.0 depends on the package, and 3.0.0 is unlisted.
            await AddAsync(CreatePackage("A", "1.0.0", downloads: 1, "A one", dependsOn: "Target"));
            await AddAsync(CreatePackage("A", "2.0.0", downloads: 2, "A two", dependsOn: null));
            await AddAsync(CreatePackage("A", "3.0.0", downloads: 4, "A three", dependsOn: null, listed: false));

            PackageDependent dependent = Assert.Single((await _target.FindDependentsAsync("Target", TestContext.Current.CancellationToken)).Data);

            Assert.Equal(7, dependent.TotalDownloads);
            Assert.Equal("A two", dependent.Description);
        }

        [Fact]
        public async Task IgnoresUnlistedVersions()
        {
            await AddAsync(CreatePackage("A", "1.0.0", downloads: 1, "A one", dependsOn: "Target", listed: false));

            Assert.Empty((await _target.FindDependentsAsync("Target", TestContext.Current.CancellationToken)).Data);
        }

        [Fact]
        public async Task MatchesThePackageIdCaseInsensitively()
        {
            await AddAsync(CreatePackage("A", "1.0.0", downloads: 1, "A one", dependsOn: "Target"));

            Assert.Single((await _target.FindDependentsAsync("TARGET", TestContext.Current.CancellationToken)).Data);
        }

        [Fact]
        public async Task ReturnsTheMostDownloadedDependents()
        {
            for (int i = 0; i < DatabaseSearchService.MaxDependents + 5; i++)
            {
                // Two versions each, so the limit has to count packages rather than versions.
                await AddAsync(CreatePackage($"P{i:D2}", "1.0.0", downloads: i, $"P{i}", dependsOn: "Target"));
                await AddAsync(CreatePackage($"P{i:D2}", "2.0.0", downloads: i, $"P{i}", dependsOn: "Target"));
            }

            IReadOnlyList<PackageDependent> data = (await _target.FindDependentsAsync("Target", TestContext.Current.CancellationToken)).Data;

            Assert.Equal(DatabaseSearchService.MaxDependents, data.Count);
            Assert.Equal(data.Count, data.Select(d => d.Id).Distinct().Count());
            Assert.Equal("P24", data[0].Id);
            Assert.Equal(48, data[0].TotalDownloads);
        }
    }

    /// <summary>
    /// Each test gets its own in-memory database, made by the migrations.
    /// </summary>
    public class FactsBase : IDisposable
    {
        private string ConnectionString { get; } = $"Data Source=DatabaseSearchServiceTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        // SQLite deletes an in-memory database when its last connection closes.
        private SqliteConnection KeepAlive { get; }
        private NuDepotDbContext Context { get; }

        private protected readonly DatabaseSearchService _target;

        public FactsBase()
        {
            KeepAlive = new SqliteConnection(ConnectionString);
            KeepAlive.Open();

            using (NuDepotDbContext context = CreateContext())
            {
                context
                    .RunMigrationsAsync(default)
                    .GetAwaiter()
                    .GetResult();
            }

            Context = CreateContext();
            _target = new DatabaseSearchService(
                Context,
                Mock.Of<IFrameworkCompatibilityService>(),
                Mock.Of<IVulnerabilityService>(),
                new SearchResponseBuilder(Mock.Of<IUrlGenerator>()));
        }

        public void Dispose()
        {
            Context.Dispose();
            KeepAlive.Dispose();
            GC.SuppressFinalize(this);
        }

        private protected NuDepotDbContext CreateContext()
        {
            DbContextOptions<NuDepotDbContext> options = new DbContextOptionsBuilder<NuDepotDbContext>()
                .UseSqlite(ConnectionString)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .Options;

            return new NuDepotDbContext(options);
        }

        protected async Task AddAsync(Package package)
        {
            using NuDepotDbContext context = CreateContext();

            await context.Packages.ExecuteInsertAsync(package, default);
        }

        protected static Package CreatePackage(string id, string version, long downloads, string description, string? dependsOn, bool listed = true)
        {
            return new Package
            {
                Id = id,
                Version = NuGetVersion.Parse(version),
                Description = description,
                Downloads = downloads,
                Listed = listed,
                PublishedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Dependencies = dependsOn == null
                    ? []
                    : [new() { Id = dependsOn, VersionRange = "[1.0.0, )", TargetFramework = "net10.0" }],
                PackageTypes = [new() { Name = "Dependency", Version = "0.0" }],
                TargetFrameworks = [new() { Moniker = "net10.0" }],
            };
        }
    }
}
