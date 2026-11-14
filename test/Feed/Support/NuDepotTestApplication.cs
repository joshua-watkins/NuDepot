using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NuDepot.Core;
using NuDepot.Core.Hosting;
using NuDepot.Feed.Configuration;
using System.Net;
using Xunit;

namespace NuDepot.Feed.Tests.Support;

/// <summary>
/// Hosts the server in memory, the way the Windows and Linux hosts do, without the website.
/// The application starts the first time it's used.
/// </summary>
internal sealed class NuDepotTestApplication : IDisposable
{
    private ITestOutputHelper Output { get; }
    private HttpClient? UpstreamClient { get; }
    private IReadOnlyDictionary<string, string?> Configuration { get; }
    private bool AddUsers { get; }
    private bool MapUsers { get; }
    private Action<IServiceCollection>? ConfigureServices { get; }
    private Lazy<WebApplication> App { get; }

    /// <param name="output">Receives the server's logs.</param>
    /// <param name="upstreamClient">If set, the server mirrors the upstream feed this client reaches.</param>
    /// <param name="configuration">The feed's configuration, which overrides the test defaults. Keys are in the "Feed" section, like "Authorization:ApiKeys:Admin".</param>
    /// <param name="users">Whether the application calls AddUsers() and MapUsers(), for users and their API keys.</param>
    /// <param name="mapUsers">Whether the application calls MapUsers(), if it isn't the same as <paramref name="users"/>.</param>
    /// <param name="configureServices">Replaces the feed's services, after they're added.</param>
    public NuDepotTestApplication(ITestOutputHelper output, HttpClient? upstreamClient = null, IReadOnlyDictionary<string, string>? configuration = null, bool users = false, bool? mapUsers = null, Action<IServiceCollection>? configureServices = null)
    {
        Output = output;
        UpstreamClient = upstreamClient;
        AddUsers = users;
        MapUsers = mapUsers ?? users;
        ConfigureServices = configureServices;
        Configuration = (configuration ?? new Dictionary<string, string>())
            .ToDictionary(setting => $"{FeedOptions.Section}:{setting.Key}", setting => (string?)setting.Value);
        App = new Lazy<WebApplication>(Start);
    }

    /// <summary>
    /// The server's clock, which tests can move forward.
    /// </summary>
    public TestClock Clock { get; } = new();

    public IServiceProvider Services => App.Value.Services;

    /// <summary>
    /// The application, as hosts pass it to <see cref="NuDepotCommands.TryRunCommandAsync"/>.
    /// </summary>
    public Microsoft.Extensions.Hosting.IHost Host => App.Value;

    public TestServer Server => App.Value.GetTestServer();

    public HttpClient CreateClient() => Server.CreateClient();

    /// <summary>
    /// Create a client that keeps the server's cookies, like a browser.
    /// </summary>
    /// <param name="cookies">Where the client keeps its cookies, so the test can see them.</param>
    public HttpClient CreateBrowserClient(CookieContainer? cookies = null)
    {
        return new HttpClient(new CookieHandler(cookies ?? new CookieContainer()) { InnerHandler = Server.CreateHandler() })
        {
            BaseAddress = Server.BaseAddress,
        };
    }

    /// <summary>
    /// Create a client that reaches the server over HTTPS, which some resources require.
    /// </summary>
    public HttpClient CreateHttpsClient()
    {
        HttpClient client = Server.CreateClient();
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    public void Dispose()
    {
        if (App.IsValueCreated)
            App.Value
                .DisposeAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
    }

    private WebApplication Start()
    {
        // Each test application gets its own in-memory database, which the server migrates at startup,
        // and a temporary storage path.
        string testId = Guid
            .NewGuid()
            .ToString("N");
        string storagePath = Path.Combine(Path.GetTempPath(), "NuDepotTests", testId, "Packages");

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.WebHost.UseTestServer();

        // NuDepot uses console logging by default. This logger throws operation
        // cancelled exceptions when the host shuts down, causing the the debugger
        // to pause repeatedly if CLR exceptions are enabled.
        builder.Logging.ClearProviders();

        // Pipe logs to the xunit output.
        builder.Logging.AddProvider(new XunitLoggerProvider(Output));

        // Setup the integration test configuration.
        Dictionary<string, string?> settings = new()
        {
            { "Feed:PackageDeletionBehavior", "HardDelete" },
            { "Feed:AllowPackageOverwrites", "true" },
            { "Feed:Database:ConnectionString", $"Data Source=NuDepotTests-{testId};Mode=Memory;Cache=Shared" },
            { "Feed:Storage:Path", storagePath },
            { "Feed:Search:Type", "Database" },
        };
        if (UpstreamClient != null)
        {
            settings["Feed:Mirroring:IsEnabled"] = "true";
            settings["Feed:Mirroring:Mirrors:0:Name"] = "Upstream";
            settings["Feed:Mirroring:Mirrors:0:PackageSource"] = "http://localhost/v3/index.json";
        }

        builder.Configuration.AddInMemoryCollection(settings);

        builder.Configuration.AddInMemoryCollection(Configuration);

        // Signing and mirrors are added like the Windows and Linux services add them: off unless their
        // "IsEnabled" settings turn them on.
        NuDepotBuilder nuDepotServices = builder.Services
            .AddNuDepot()
            .AddFeed()
            .EnableSigning()
            .EnableMirroring();
        if (AddUsers)
            nuDepotServices.AddUsers();

        // One clock for the feed and its cookies, which tests can move forward.
        builder.Services.AddSingleton<TimeProvider>(Clock);
        builder.Services.Configure<CookieAuthenticationOptions>(
            NuDepotAuthenticationSchemes.User,
            options => options.TimeProvider = Clock);

        if (UpstreamClient != null)
            builder.Services.AddSingleton(UpstreamClient);

        ConfigureServices?.Invoke(builder.Services);

        WebApplication app = builder.Build();

        NuDepotApplicationBuilder nuDepot = app
            .UseNuDepot()
            .UseFeed();
        if (MapUsers)
            nuDepot.MapUsers();

        try
        {
            app
                .RunMigrationsAsync()
                .GetAwaiter()
                .GetResult();
            app
                .StartAsync()
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            app
                .DisposeAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
            throw;
        }

        return app;
    }
}
