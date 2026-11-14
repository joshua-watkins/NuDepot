using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuGet.Versioning;
using Xunit;

namespace NuDepot.Feed.Tests.Services;

public class PackageDatabaseTests
{
    public class AddAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsPackageAlreadyExistsOnUniqueConstraintViolation()
        {
            using NuDepotDbContext context = CreateContext();
            PackageDatabase target = new(context);

            PackageAddResult result = await target.AddAsync(CreatePackage("Package", "1.0.0"), TestContext.Current.CancellationToken);

            Assert.Equal(PackageAddResult.PackageAlreadyExists, result);
        }

        [Fact]
        public async Task AddsPackage()
        {
            PackageAddResult result = await _target.AddAsync(CreatePackage("New.Package", "2.0.0"), TestContext.Current.CancellationToken);

            Assert.Equal(PackageAddResult.Success, result);
            Assert.True(await _target.ExistsAsync("New.Package", NuGetVersion.Parse("2.0.0"), TestContext.Current.CancellationToken));
        }
    }

    public class ExistsAsync : FactsBase
    {
        [Theory]
        [InlineData("Package", "1.0.0", true)]
        [InlineData("PACKAGE", "1.0.0", true)]
        [InlineData("Package", "1.0.0.0", true)]
        [InlineData("Unlisted.Package", "1.0.0", true)]
        [InlineData("Package", "2.0.0", false)]
        [InlineData("Fake.Package", "1.0.0", false)]
        public async Task ReturnsTrueIfPackageExists(string packageId, string packageVersion, bool exists)
        {
            Assert.Equal(exists, await _target.ExistsAsync(packageId, NuGetVersion.Parse(packageVersion), TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData("Package", true)]
        [InlineData("Unlisted.Package", true)]
        [InlineData("Fake.Package", false)]
        public async Task ReturnsTrueIfAnyVersionExists(string packageId, bool exists)
        {
            Assert.Equal(exists, await _target.ExistsAsync(packageId, TestContext.Current.CancellationToken));
        }
    }

    public class FindAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsEmptyListIfPackageDoesNotExist()
        {
            Assert.Empty(await _target.FindAsync("Fake.Package", includeUnlisted: true, TestContext.Current.CancellationToken));
        }

        [Theory]
        [MemberData(nameof(ReturnsPackagesData))]
        public async Task ReturnsPackages(string packageId, bool includeUnlisted, bool exists)
        {
            IReadOnlyList<Package> packages = await _target.FindAsync(packageId, includeUnlisted, TestContext.Current.CancellationToken);

            if (exists)
            {
                Package package = Assert.Single(packages);
                Assert.Equal("1.0.0", package.NormalizedVersionString);
                Assert.Single(package.PackageTypes);
                Assert.Single(package.TargetFrameworks);
            }
            else
            {
                Assert.Empty(packages);
            }
        }

        public static TheoryData<string, bool, bool> ReturnsPackagesData => new()
        {
            // A package that doesn't exist should never be returned
            { "Fake.Package", true, false },

            // A listed package should be returned regardless of the "includeUnlisted" parameter
            { "Package", false, true },
            { "Package", true, true },

            // Unlisted packages should only be returned if "includeUnlisted" is true
            { "Unlisted.Package", false, false },
            { "Unlisted.Package", true, true },
        };
    }

    public class FindOrNullAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsNullIfPackageDoesNotExist()
        {
            Assert.Null(await _target.FindOrNullAsync("Package", NuGetVersion.Parse("2.0.0"), includeUnlisted: true, TestContext.Current.CancellationToken));
        }

        [Theory]
        [MemberData(nameof(ReturnsPackageData))]
        public async Task ReturnsPackage(string packageId, string packageVersion, bool includeUnlisted, bool exists)
        {
            Package? package = await _target.FindOrNullAsync(packageId, NuGetVersion.Parse(packageVersion), includeUnlisted, TestContext.Current.CancellationToken);

            Assert.Equal(exists, package != null);
            Assert.Equal(exists ? "1.0.0" : null, package?.NormalizedVersionString);
        }

        public static TheoryData<string, string, bool, bool> ReturnsPackageData => new()
        {
            // A package that doesn't exist should never be returned
            { "Fake.Package", "1.0.0", true, false },

            // A listed package should be returned regardless of the "includeUnlisted" parameter
            { "Package", "1.0.0", false, true },
            { "Package", "1.0.0", true, true },

            // The inputted package version should be normalized
            { "Package", "1.0.0.0", false, true },

            // Unlisted packages should only be returned if "includeUnlisted" is true
            { "Unlisted.Package", "1.0.0", false, false },
            { "Unlisted.Package", "1.0.0", true, true },
        };
    }

    public class UnlistPackageAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsFalseIfPackageDoesNotExist()
        {
            Assert.False(await _target.UnlistPackageAsync("Fake.Package", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData("Package")]
        [InlineData("Unlisted.Package")]
        public async Task UnlistsPackage(string packageId)
        {
            Assert.True(await _target.UnlistPackageAsync(packageId, NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken));

            Assert.False((await FindAsync(packageId)).Listed);
        }
    }

    public class RelistPackageAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsFalseIfPackageDoesNotExist()
        {
            Assert.False(await _target.RelistPackageAsync("Fake.Package", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData("Package")]
        [InlineData("Unlisted.Package")]
        public async Task RelistsPackage(string packageId)
        {
            Assert.True(await _target.RelistPackageAsync(packageId, NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken));

            Assert.True((await FindAsync(packageId)).Listed);
        }
    }

    public class AddDownloadAsync : FactsBase
    {
        [Fact]
        public async Task IgnoresPackageThatDoesNotExist()
        {
            await _target.AddDownloadAsync("Fake.Package", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken);

            Assert.False(await _target.ExistsAsync("Fake.Package", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task IncrementsPackageDownloads()
        {
            await _target.AddDownloadAsync("Package", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken);
            await _target.AddDownloadAsync("Package", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken);

            Assert.Equal(2, (await FindAsync("Package")).Downloads);
        }
    }

    public class HardDeletePackageAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsFalseIfPackageDoesNotExist()
        {
            Assert.False(await _target.HardDeletePackageAsync("Fake.Package", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task DeletesPackage()
        {
            Assert.True(await _target.HardDeletePackageAsync("Package", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken));

            Assert.False(await _target.ExistsAsync("Package", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task DeletesDependencies()
        {
            Package package = CreatePackage("Dependent.Package", "1.0.0");
            package.Dependencies = [new PackageDependency { Id = "Package", VersionRange = "[1.0.0, )", TargetFramework = "net10.0" }];
            Assert.Equal(PackageAddResult.Success, await _target.AddAsync(package, TestContext.Current.CancellationToken));

            Assert.True(await _target.HardDeletePackageAsync("Dependent.Package", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken));

            using NuDepotDbContext context = CreateContext();
            Assert.False(await context.PackageDependencies.AnyAsync(TestContext.Current.CancellationToken));
        }
    }

    public class AllRepositorySignedAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsFalseIfAnyPackageIsUnsigned()
        {
            Assert.False(await _target.AllRepositorySignedAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ReturnsTrueIfEveryPackageIsSigned()
        {
            using (NuDepotDbContext context = CreateContext())
            {
                await context.Packages.ExecuteUpdateAsync(setters => setters.SetProperty(p => p.IsRepositorySigned, true), TestContext.Current.CancellationToken);
            }

            Assert.True(await _target.AllRepositorySignedAsync(TestContext.Current.CancellationToken));
        }
    }

    /// <summary>
    /// Each test gets its own in-memory database, made by the migrations, with a listed
    /// package "Package" and an unlisted package "Unlisted.Package", both version 1.0.0.
    /// </summary>
    public class FactsBase : IDisposable
    {
        private string ConnectionString { get; } = $"Data Source=PackageDatabaseTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        // SQLite deletes an in-memory database when its last connection closes.
        private SqliteConnection KeepAlive { get; }
        private NuDepotDbContext Context { get; }

        private protected readonly PackageDatabase _target;

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

                Package unlisted = CreatePackage("Unlisted.Package", "1.0.0");
                unlisted.Listed = false;

                context.Packages
                    .ExecuteInsertAsync([CreatePackage("Package", "1.0.0"), unlisted], default)
                    .GetAwaiter()
                    .GetResult();
            }

            Context = CreateContext();
            _target = new PackageDatabase(Context);
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

        /// <summary>
        /// Read a version 1.0.0 package from the database, with a context of its own.
        /// </summary>
        protected async Task<Package> FindAsync(string packageId)
        {
            using NuDepotDbContext context = CreateContext();

            return await context.Packages.SingleAsync(p => p.Id == packageId);
        }

        protected static Package CreatePackage(string id, string version)
        {
            return new Package
            {
                Id = id,
                Version = NuGetVersion.Parse(version),
                Listed = true,
                PublishedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                PackageTypes = [new() { Name = "Dependency", Version = "0.0" }],
                TargetFrameworks = [new() { Moniker = "net10.0" }],
            };
        }
    }
}
