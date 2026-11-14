using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Search;
using NuDepot.Feed.Storage;
using NuDepot.Feed.Tests.Support;
using Xunit;

namespace NuDepot.Feed.Tests;

public class HostIntegrationTests
{
    private string ConnectionStringKey { get; } = "Feed:Database:ConnectionString";

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void VerboseLoggingToTheEventLogIsAnOption()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Event Log is only on Windows.");

        using ServiceProvider before = BuildLogging("Windows", uncomment: false, AddEventLog);
        using ServiceProvider after = BuildLogging("Windows", uncomment: true, AddEventLog);

        Assert.False(IsLogged(before, "NuDepot.Feed.Indexing.PackageIndexingService", LogLevel.Information));
        Assert.True(IsLogged(before, "NuDepot.Feed.Indexing.PackageIndexingService", LogLevel.Warning));

        // NuDepot's own lines, but not ASP.NET Core's for each request, or its authentication handler's.
        Assert.True(IsLogged(after, "NuDepot.Feed.Indexing.PackageIndexingService", LogLevel.Information));
        Assert.True(IsLogged(after, "NuDepot.Feed.Users.UserService", LogLevel.Information));
        Assert.False(IsLogged(after, "Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Information));
        Assert.False(IsLogged(after, "NuDepot.Feed.Authentication.ApiKeyAuthenticationHandler", LogLevel.Information));
        Assert.True(IsLogged(after, "NuDepot.Feed.Authentication.ApiKeyAuthenticationHandler", LogLevel.Warning));
    }

    [Fact]
    public void LoggingOnlyWarningsToTheJournalIsAnOption()
    {
        using ServiceProvider before = BuildLogging("Linux", uncomment: false, logging => logging.AddProvider(new LogRecorder()));
        using ServiceProvider after = BuildLogging("Linux", uncomment: true, logging => logging.AddProvider(new LogRecorder()));

        Assert.True(IsLogged(before, "NuDepot.Feed.Indexing.PackageIndexingService", LogLevel.Information));
        Assert.False(IsLogged(before, "Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Information));

        Assert.False(IsLogged(after, "NuDepot.Feed.Indexing.PackageIndexingService", LogLevel.Information));
        Assert.True(IsLogged(after, "NuDepot.Feed.Indexing.PackageIndexingService", LogLevel.Warning));
    }

    // The services' logging, with the host's appsettings.json, and its commented-out Logging section uncommented.
    private static ServiceProvider BuildLogging(string host, bool uncomment, Action<ILoggingBuilder> providers)
    {
        string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Hosts", host, "appsettings.json"));
        if (uncomment)
        {
            int start = Array.FindIndex(lines, line => line.TrimStart().StartsWith("//\"Logging\"", StringComparison.Ordinal));
            Assert.True(start >= 0, "The template has a Logging section to uncomment.");
            for (int i = start; i < lines.Length && lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal); i++)
                lines[i] = lines[i].Remove(lines[i].IndexOf("//", StringComparison.Ordinal), 2);
        }

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // The services' defaults, from NuDepotService.
                ["Logging:LogLevel:Default"] = "Information",
                ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",
                ["Logging:LogLevel:NuDepot.Feed.Authentication"] = "Warning",
            })
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines))))
            .Build();

        return new ServiceCollection()
            .AddLogging(logging =>
            {
                providers(logging);
                logging.AddConfiguration(configuration.GetSection("Logging"));
            })
            .BuildServiceProvider();
    }

    // Like WebApplication on Windows: the Event Log keeps warnings, unless the settings say otherwise.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AddEventLog(ILoggingBuilder logging)
    {
        logging
            .AddEventLog()
            .AddFilter<EventLogLoggerProvider>(level => level >= LogLevel.Warning);
    }

    private static bool IsLogged(IServiceProvider services, string category, LogLevel level)
        => services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(category)
            .IsEnabled(level);

    [Fact]
    public void PackagesAreStoredInFiles()
    {
        IServiceProvider provider = BuildServiceProvider();

        Assert.IsType<FileStorageService>(provider.GetRequiredService<IStorageService>());
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
        IServiceProvider provider = BuildServiceProvider(configure: options => options.Search.Type = SearchType.Null);

        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<NullSearchService>(scope.ServiceProvider.GetRequiredService<ISearchService>());
    }

    [Fact]
    public void ReturnsDatabaseContext()
    {
        IServiceProvider provider = BuildServiceProvider(new Dictionary<string, string?>
        {
            { ConnectionStringKey, "..." }
        });

        Assert.NotNull(provider.GetRequiredService<NuDepotDbContext>());
    }

    [Fact]
    public void ReturnsSqliteContext()
    {
        IServiceProvider provider = BuildServiceProvider(new Dictionary<string, string?>
        {
            { ConnectionStringKey, "..." }
        });

        Assert.True(provider.GetRequiredService<NuDepotDbContext>().Database.IsSqlite());
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
            NuDepotDbContext context = scope.ServiceProvider.GetRequiredService<NuDepotDbContext>();
            await context.RunMigrationsAsync(TestContext.Current.CancellationToken);
            await context.VulnerabilityAdvisories.ExecuteInsertAsync(
                new VulnerabilityAdvisory
                {
                    PackageId = "Package",
                    VersionRange = "[1.0.0]",
                    AdvisoryUrl = "https://example.com/advisory",
                },
                TestContext.Current.CancellationToken);
        }

        using (IServiceScope scope = provider.CreateScope())
        {
            NuDepotDbContext context = scope.ServiceProvider.GetRequiredService<NuDepotDbContext>();
            Assert.Equal(1, await context.VulnerabilityAdvisories.CountAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task DatabaseFilesUseWriteAheadLogging()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(folder, "nudepot.db");
        try
        {
            ServiceProvider provider = (ServiceProvider)BuildServiceProvider(new Dictionary<string, string?>
            {
                { ConnectionStringKey, $"Data Source={databasePath};Pooling=false" }
            });

            await using (provider)
            {
                using IServiceScope scope = provider.CreateScope();
                NuDepotDbContext context = scope.ServiceProvider.GetRequiredService<NuDepotDbContext>();
                await context.RunMigrationsAsync(TestContext.Current.CancellationToken);
            }

            // A new connection reads the mode from the file.
            await using SqliteConnection connection = new($"Data Source={databasePath};Pooling=false");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("wal", await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
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

    [Theory]
    [InlineData("Windows", false)]
    [InlineData("Windows", true)]
    [InlineData("Linux", false)]
    [InlineData("Linux", true)]
    public void HostSettingsSetUpTheFeaturesButLeaveThemToTheInstaller(string host, bool installerTurnedThemOn)
    {
        // The installer's choices are in setup.ini, which the services read before appsettings.json.
        Dictionary<string, string?> setup = installerTurnedThemOn
            ? new()
            {
                ["Feed:Authorization:Users:IsEnabled"] = "true",
                ["Feed:RepositorySigning:IsEnabled"] = "true",
                ["Feed:Mirroring:IsEnabled"] = "true",
            }
            : [];
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(setup)
            .SetBasePath(Path.Combine(AppContext.BaseDirectory, "Hosts", host))
            .AddJsonFile("appsettings.json")
            .Build();

        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(configuration);
        services
            .AddNuDepot()
            .AddFeed()
            .EnableUsers()
            .EnableSigning()
            .EnableMirroring();
        using ServiceProvider provider = services.BuildServiceProvider();

        UserOptions users = provider.GetRequiredService<IOptions<UserOptions>>().Value;
        MirroringOptions mirroring = provider.GetRequiredService<IOptions<MirroringOptions>>().Value;
        Assert.Equal(installerTurnedThemOn, users.IsEnabled);
        Assert.True(users.AnonymousReads);
        Assert.Equal(installerTurnedThemOn, provider.GetRequiredService<IOptions<RepositorySigningOptions>>().Value.IsEnabled);
        Assert.Equal(installerTurnedThemOn, mirroring.IsEnabled);
        Assert.Equal(MirrorOptions.NuGetPackageSource, Assert.Single(mirroring.Mirrors).PackageSource);

        // No keys, so the feed is open. The example keys are commented out: everyone who installs NuDepot has them.
        ApiKeyOptions keys = provider.GetRequiredService<IOptions<FeedOptions>>().Value.Authorization.ApiKeys;
        Assert.Null(keys.ReadOnly);
        Assert.Null(keys.ReadWrite);
        Assert.Null(keys.Admin);
    }

    [Fact]
    public void PackagesAreReadWithAQueryForEachCollection()
    {
        using ServiceProvider provider = BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        // One query with a row for every combination of a package's dependencies, types and frameworks grows quickly.
        RelationalOptionsExtension relational = RelationalOptionsExtension.Extract(scope.ServiceProvider
            .GetRequiredService<NuDepotDbContext>()
            .GetService<IDbContextOptions>());

        Assert.Equal(QuerySplittingBehavior.SplitQuery, relational.QuerySplittingBehavior);
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

    private ServiceProvider BuildServiceProvider(Dictionary<string, string?>? configs = null, Action<FeedOptions>? configure = null)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { ConnectionStringKey, "Data Source=nuget.db" },
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
