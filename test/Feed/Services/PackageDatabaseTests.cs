using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NuDepot.Feed.Entities;
using NuGet.Versioning;
using Xunit;

namespace NuDepot.Feed.Tests.Services
{
    public class PackageDatabaseTests
    {
        public class AddAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsPackageAlreadyExistsOnUniqueConstraintViolation()
            {
                using var context = CreateContext();
                var target = new PackageDatabase(context);

                var result = await target.AddAsync(CreatePackage("Package", "1.0.0"), default);

                Assert.Equal(PackageAddResult.PackageAlreadyExists, result);
            }

            [Fact]
            public async Task AddsPackage()
            {
                var result = await _target.AddAsync(CreatePackage("New.Package", "2.0.0"), default);

                Assert.Equal(PackageAddResult.Success, result);
                Assert.True(await _target.ExistsAsync("New.Package", NuGetVersion.Parse("2.0.0"), default));
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
                Assert.Equal(exists, await _target.ExistsAsync(packageId, NuGetVersion.Parse(packageVersion), default));
            }

            [Theory]
            [InlineData("Package", true)]
            [InlineData("Unlisted.Package", true)]
            [InlineData("Fake.Package", false)]
            public async Task ReturnsTrueIfAnyVersionExists(string packageId, bool exists)
            {
                Assert.Equal(exists, await _target.ExistsAsync(packageId, default));
            }
        }

        public class FindAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsEmptyListIfPackageDoesNotExist()
            {
                Assert.Empty(await _target.FindAsync("Fake.Package", includeUnlisted: true, default));
            }

            [Theory]
            [MemberData(nameof(ReturnsPackagesData))]
            public async Task ReturnsPackages(string packageId, bool includeUnlisted, bool exists)
            {
                var packages = await _target.FindAsync(packageId, includeUnlisted, default);

                if (exists)
                {
                    var package = Assert.Single(packages);
                    Assert.Equal("1.0.0", package.NormalizedVersionString);
                    Assert.Single(package.PackageTypes);
                    Assert.Single(package.TargetFrameworks);
                }
                else
                {
                    Assert.Empty(packages);
                }
            }

            public static IEnumerable<object[]> ReturnsPackagesData()
            {
                // A package that doesn't exist should never be returned
                yield return new object[] { "Fake.Package", true, false };

                // A listed package should be returned regardless of the "includeUnlisted" parameter
                yield return new object[] { "Package", false, true };
                yield return new object[] { "Package", true, true };

                // Unlisted packages should only be returned if "includeUnlisted" is true
                yield return new object[] { "Unlisted.Package", false, false };
                yield return new object[] { "Unlisted.Package", true, true };
            }
        }

        public class FindOrNullAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsNullIfPackageDoesNotExist()
            {
                Assert.Null(await _target.FindOrNullAsync("Package", NuGetVersion.Parse("2.0.0"), includeUnlisted: true, default));
            }

            [Theory]
            [MemberData(nameof(ReturnsPackageData))]
            public async Task ReturnsPackage(string packageId, string packageVersion, bool includeUnlisted, bool exists)
            {
                var package = await _target.FindOrNullAsync(packageId, NuGetVersion.Parse(packageVersion), includeUnlisted, default);

                Assert.Equal(exists, package != null);
                Assert.Equal(exists ? "1.0.0" : null, package?.NormalizedVersionString);
            }

            public static IEnumerable<object[]> ReturnsPackageData()
            {
                // A package that doesn't exist should never be returned
                yield return new object[] { "Fake.Package", "1.0.0", true, false };

                // A listed package should be returned regardless of the "includeUnlisted" parameter
                yield return new object[] { "Package", "1.0.0", false, true };
                yield return new object[] { "Package", "1.0.0", true, true };

                // The inputted package version should be normalized
                yield return new object[] { "Package", "1.0.0.0", false, true };

                // Unlisted packages should only be returned if "includeUnlisted" is true
                yield return new object[] { "Unlisted.Package", "1.0.0", false, false };
                yield return new object[] { "Unlisted.Package", "1.0.0", true, true };
            }
        }

        public class UnlistPackageAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsFalseIfPackageDoesNotExist()
            {
                Assert.False(await _target.UnlistPackageAsync("Fake.Package", NuGetVersion.Parse("1.0.0"), default));
            }

            [Theory]
            [InlineData("Package")]
            [InlineData("Unlisted.Package")]
            public async Task UnlistsPackage(string packageId)
            {
                Assert.True(await _target.UnlistPackageAsync(packageId, NuGetVersion.Parse("1.0.0"), default));

                Assert.False((await FindAsync(packageId)).Listed);
            }
        }

        public class RelistPackageAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsFalseIfPackageDoesNotExist()
            {
                Assert.False(await _target.RelistPackageAsync("Fake.Package", NuGetVersion.Parse("1.0.0"), default));
            }

            [Theory]
            [InlineData("Package")]
            [InlineData("Unlisted.Package")]
            public async Task RelistsPackage(string packageId)
            {
                Assert.True(await _target.RelistPackageAsync(packageId, NuGetVersion.Parse("1.0.0"), default));

                Assert.True((await FindAsync(packageId)).Listed);
            }
        }

        public class AddDownloadAsync : FactsBase
        {
            [Fact]
            public async Task IgnoresPackageThatDoesNotExist()
            {
                await _target.AddDownloadAsync("Fake.Package", NuGetVersion.Parse("1.0.0"), default);

                Assert.False(await _target.ExistsAsync("Fake.Package", default));
            }

            [Fact]
            public async Task IncrementsPackageDownloads()
            {
                await _target.AddDownloadAsync("Package", NuGetVersion.Parse("1.0.0"), default);
                await _target.AddDownloadAsync("Package", NuGetVersion.Parse("1.0.0"), default);

                Assert.Equal(2, (await FindAsync("Package")).Downloads);
            }
        }

        public class HardDeletePackageAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsFalseIfPackageDoesNotExist()
            {
                Assert.False(await _target.HardDeletePackageAsync("Fake.Package", NuGetVersion.Parse("1.0.0"), default));
            }

            [Fact]
            public async Task DeletesPackage()
            {
                Assert.True(await _target.HardDeletePackageAsync("Package", NuGetVersion.Parse("1.0.0"), default));

                Assert.False(await _target.ExistsAsync("Package", default));
            }
        }

        public class AllRepositorySignedAsync : FactsBase
        {
            [Fact]
            public async Task ReturnsFalseIfAnyPackageIsUnsigned()
            {
                Assert.False(await _target.AllRepositorySignedAsync(default));
            }

            [Fact]
            public async Task ReturnsTrueIfEveryPackageIsSigned()
            {
                using (var context = CreateContext())
                {
                    foreach (var package in await context.Packages.ToListAsync()) package.IsRepositorySigned = true;
                    await context.SaveChangesAsync();
                }

                Assert.True(await _target.AllRepositorySignedAsync(default));
            }
        }

        /// <summary>
        /// Each test gets its own in-memory database, made by the migrations, with a listed
        /// package "Package" and an unlisted package "Unlisted.Package", both version 1.0.0.
        /// </summary>
        public class FactsBase : IDisposable
        {
            private readonly string _connectionString = $"Data Source=PackageDatabaseTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

            // SQLite deletes an in-memory database when its last connection closes.
            private readonly SqliteConnection _keepAlive;
            private readonly NuDepotContext _context;

            protected readonly PackageDatabase _target;

            public FactsBase()
            {
                _keepAlive = new SqliteConnection(_connectionString);
                _keepAlive.Open();

                using (var context = CreateContext())
                {
                    context.RunMigrationsAsync(default).GetAwaiter().GetResult();

                    var unlisted = CreatePackage("Unlisted.Package", "1.0.0");
                    unlisted.Listed = false;

                    context.Packages.Add(CreatePackage("Package", "1.0.0"));
                    context.Packages.Add(unlisted);
                    context.SaveChanges();
                }

                _context = CreateContext();
                _target = new PackageDatabase(_context);
            }

            public void Dispose()
            {
                _context.Dispose();
                _keepAlive.Dispose();
            }

            protected NuDepotContext CreateContext()
            {
                var options = new DbContextOptionsBuilder<NuDepotContext>()
                    .UseSqlite(_connectionString)
                    .Options;

                return new NuDepotContext(options);
            }

            /// <summary>
            /// Read a version 1.0.0 package from the database, without the target's tracked entities.
            /// </summary>
            protected async Task<Package> FindAsync(string packageId)
            {
                using var context = CreateContext();

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
                    PackageTypes = new List<PackageType> { new PackageType { Name = "Dependency", Version = "0.0" } },
                    TargetFrameworks = new List<TargetFramework> { new TargetFramework { Moniker = "net10.0" } },
                };
            }
        }
    }
}
