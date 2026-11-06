using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Search;
using NuDepot.Feed.Storage;
using Xunit;

namespace NuDepot.Feed.Tests;

public class HostIntegrationTests
{
    private string StorageTypeKey { get; } = "Feed:Storage:Type";
    private string ConnectionStringKey { get; } = "Feed:Database:ConnectionString";

    [Fact]
    public void ThrowsIfStorageTypeInvalid()
    {
        IServiceProvider provider = BuildServiceProvider(new Dictionary<string, string?>
        {
            { StorageTypeKey, "InvalidType" }
        });

        Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<IStorageService>());
    }

    [Theory]
    [InlineData("FileSystem", typeof(FileStorageService))]
    [InlineData("Null", typeof(NullStorageService))]
    public void StorageTypeChoosesTheStorage(string type, Type expected)
    {
        IServiceProvider provider = BuildServiceProvider(new Dictionary<string, string?>
        {
            { StorageTypeKey, type }
        });

        Assert.IsType(expected, provider.GetRequiredService<IStorageService>());
    }

    [Theory]
    [InlineData("Database", typeof(DatabaseSearchService))]
    [InlineData("Null", typeof(NullSearchService))]
    public void SearchTypeChoosesTheSearch(string type, Type expected)
    {
        IServiceProvider provider = BuildServiceProvider(new Dictionary<string, string?>
        {
            { "Feed:Search:Type", type }
        });

        using IServiceScope scope = provider.CreateScope();
        Assert.IsType(expected, scope.ServiceProvider.GetRequiredService<ISearchService>());
    }

    [Fact]
    public void ConfigureChangesTheBoundOptions()
    {
        IServiceProvider provider = BuildServiceProvider(configure: options => options.Storage.Type = StorageType.Null);

        Assert.IsType<NullStorageService>(provider.GetRequiredService<IStorageService>());
    }

    [Fact]
    public void ReturnsDatabaseContext()
    {
        IServiceProvider provider = BuildServiceProvider(new Dictionary<string, string?>
        {
            { ConnectionStringKey, "..." }
        });

        Assert.NotNull(provider.GetRequiredService<NuDepotContext>());
    }

    [Fact]
    public void ReturnsSqliteContext()
    {
        IServiceProvider provider = BuildServiceProvider(new Dictionary<string, string?>
        {
            { ConnectionStringKey, "..." }
        });

        Assert.True(provider.GetRequiredService<NuDepotContext>().Database.IsSqlite());
    }

    [Fact]
    public async Task KeepsInMemoryDatabaseBetweenContexts()
    {
        IServiceProvider provider = BuildServiceProvider(new Dictionary<string, string?>
        {
            { ConnectionStringKey, $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared" }
        });

        using (IServiceScope scope = provider.CreateScope())
        {
            NuDepotContext context = scope.ServiceProvider.GetRequiredService<NuDepotContext>();
            await context.RunMigrationsAsync(default);
            await context.VulnerabilityAdvisories.ExecuteInsertAsync(
                new VulnerabilityAdvisory
                {
                    PackageId = "Package",
                    VersionRange = "[1.0.0]",
                    AdvisoryUrl = "https://example.com/advisory",
                },
                default);
        }

        using (IServiceScope scope = provider.CreateScope())
        {
            NuDepotContext context = scope.ServiceProvider.GetRequiredService<NuDepotContext>();
            Assert.Equal(1, await context.VulnerabilityAdvisories.CountAsync());
        }
    }

    [Theory]
    [InlineData("Windows")]
    [InlineData("Linux")]
    public void HostsUseInMemoryDatabaseAndTemporaryStorageInDevelopment(string host)
    {
        // The hosts' appsettings files are copied to the test output's Hosts folder.
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(AppContext.BaseDirectory, "Hosts", host))
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json")
            .Build();

        Assert.Contains("Mode=Memory", configuration[ConnectionStringKey]);
        Assert.True(configuration.GetValue<bool>("Feed:Storage:Temporary"));
    }

    [Fact]
    public void TemporaryStorageUsesNewFolderThatIsDeletedOnShutdown()
    {
        ServiceProvider provider = (ServiceProvider)BuildServiceProvider(new Dictionary<string, string?>
        {
            { "Feed:Storage:Temporary", "true" },
        });

        string? path = provider.GetRequiredService<IOptions<FeedOptions>>().Value.Storage.Path;

        Assert.NotNull(path);
        Assert.StartsWith(Path.GetTempPath(), path);
        Assert.True(Directory.Exists(path));

        provider.Dispose();

        Assert.False(Directory.Exists(path));
    }

    private IServiceProvider BuildServiceProvider(Dictionary<string, string?>? configs = null, Action<FeedOptions>? configure = null)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { ConnectionStringKey, "Data Source=nuget.db" },
                { StorageTypeKey, "FileSystem" },
                { "Feed:Search:Type", "Database" },
            })
            .AddInMemoryCollection(configs ?? [])
            .Build();

        ServiceCollection services = new();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services
            .AddNuDepot()
            .AddFeed(configure);

        return services.BuildServiceProvider();
    }
}
