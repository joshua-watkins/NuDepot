using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using Xunit;

namespace NuDepot.Feed.Tests
{
    public class HostIntegrationTests
    {
        private readonly string DatabaseTypeKey = "Database:Type";
        private readonly string ConnectionStringKey = "Database:ConnectionString";

        [Fact]
        public void ThrowsIfDatabaseTypeInvalid()
        {
            var provider = BuildServiceProvider(new Dictionary<string, string?>
            {
                { DatabaseTypeKey, "InvalidType" }
            });

            Assert.Throws<InvalidOperationException>(
                () => provider.GetRequiredService<NuDepotContext>());
        }

        [Fact]
        public void ReturnsDatabaseContext()
        {
            var provider = BuildServiceProvider(new Dictionary<string, string?>
            {
                { DatabaseTypeKey, "Sqlite" },
                { ConnectionStringKey, "..." }
            });

            Assert.NotNull(provider.GetRequiredService<NuDepotContext>());
        }

        [Fact]
        public void ReturnsSqliteContext()
        {
            var provider = BuildServiceProvider(new Dictionary<string, string?>
            {
                { DatabaseTypeKey, "Sqlite" },
                { ConnectionStringKey, "..." }
            });

            Assert.True(provider.GetRequiredService<NuDepotContext>().Database.IsSqlite());
        }

        [Fact]
        public async Task KeepsInMemoryDatabaseBetweenContexts()
        {
            var provider = BuildServiceProvider(new Dictionary<string, string?>
            {
                { DatabaseTypeKey, "Sqlite" },
                { ConnectionStringKey, $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared" }
            });

            using (var scope = provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<NuDepotContext>();
                await context.RunMigrationsAsync(default);
                context.VulnerabilityAdvisories.Add(new VulnerabilityAdvisory
                {
                    PackageId = "Package",
                    VersionRange = "[1.0.0]",
                    AdvisoryUrl = "https://example.com/advisory",
                });
                await context.SaveChangesAsync();
            }

            using (var scope = provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<NuDepotContext>();
                Assert.Equal(1, await context.VulnerabilityAdvisories.CountAsync());
            }
        }

        [Theory]
        [InlineData("Windows")]
        [InlineData("Linux")]
        public void HostsUseInMemoryDatabaseAndTemporaryStorageInDevelopment(string host)
        {
            // The hosts' appsettings files are copied to the test output's Hosts folder.
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Path.Combine(AppContext.BaseDirectory, "Hosts", host))
                .AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.Development.json")
                .Build();

            Assert.Contains("Mode=Memory", configuration[ConnectionStringKey]);
            Assert.True(configuration.GetValue<bool>("Storage:Temporary"));
        }

        [Fact]
        public void TemporaryStorageUsesNewFolderThatIsDeletedOnShutdown()
        {
            var provider = (ServiceProvider)BuildServiceProvider(new Dictionary<string, string?>
            {
                { "Storage:Temporary", "true" },
            });

            var path = provider.GetRequiredService<IOptions<FileSystemStorageOptions>>().Value.Path;

            Assert.NotNull(path);
            Assert.StartsWith(Path.GetTempPath(), path);
            Assert.True(Directory.Exists(path));

            provider.Dispose();

            Assert.False(Directory.Exists(path));
        }

        private IServiceProvider BuildServiceProvider(Dictionary<string, string?>? configs = null)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { DatabaseTypeKey, "Sqlite" },
                    { ConnectionStringKey, "Data Source=nuget.db" },
                    { "Storage:Type", "FileSystem" },
                    { "Search:Type", "Database" },
                })
                .AddInMemoryCollection(configs ?? new Dictionary<string, string?>())
                .Build();

            var services = new ServiceCollection();

            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging();
            services.AddNuDepot(feed =>
            {
                feed.AddSqliteDatabase();
                feed.AddFileStorage();
            });

            return services.BuildServiceProvider();
        }
    }
}
