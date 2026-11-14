using Microsoft.Extensions.Configuration.Ini;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.FileProviders;

namespace NuDepot;

/// <summary>
/// How the Linux service starts. The Windows service has a copy, so keep them alike. The application's folder only holds the application, so
/// upgrades can replace it. The settings are in a folder of their own, which is also the working directory and
/// the content root, so relative paths in the settings, like a signing certificate's or Kestrel's HTTPS
/// certificate's, are relative to it. Settings are optional:
/// the options have defaults, which setup.ini (the installer's choices) and then nudepot.json override.
/// </summary>
internal static partial class NuDepotService
{
    /// <summary>
    /// The environment variable that holds the settings folder, if it isn't the service's usual one.
    /// </summary>
    public const string SettingsFolderVariable = "NUDEPOT_CONFIG_ROOT";

    /// <summary>
    /// The administrator's settings, in place of ASP.NET Core's appsettings.json, named for NuDepot.
    /// </summary>
    public const string SettingsFile = "nudepot.json";

    /// <summary>
    /// The installer's choices, like the port, which nudepot.json overrides.
    /// </summary>
    public const string SetupFile = "setup.ini";

    /// <summary>
    /// The port the service listens on, on localhost, unless the settings give addresses.
    /// </summary>
    public const int DefaultPort = 3333;

    /// <summary>
    /// Creates the application's builder, with the settings in <paramref name="settingsFolder"/>.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <param name="settingsFolder">The folder that holds nudepot.json and setup.ini, and is the working directory.</param>
    /// <param name="defaults">This platform's defaults, which any setting overrides.</param>
    /// <returns>The builder.</returns>
    public static WebApplicationBuilder CreateBuilder(string[] args, string settingsFolder, IReadOnlyDictionary<string, string?>? defaults = null)
    {
        // During development, the settings are the project's, in the application's folder, and the working
        // directory is left alone.
        WebApplicationBuilder builder = Create(args, AppContext.BaseDirectory);
        if (!builder.Environment.IsDevelopment())
        {
            // The settings folder is the content root too, so the settings' relative paths, like Kestrel's
            // certificates', are relative to it. The content root is fixed once the builder is created.
            ((IDisposable)builder.Configuration).Dispose();

            string folder = Environment.GetEnvironmentVariable(SettingsFolderVariable) is { Length: > 0 } configured
                ? Path.GetFullPath(configured)
                : settingsFolder;
            Directory.CreateDirectory(folder);
            Directory.SetCurrentDirectory(folder);

            builder = Create(args, folder);
        }

        ReadSettingsFrom(builder.Configuration, builder.Environment.ContentRootPath, setup: !builder.Environment.IsDevelopment());

        // Below everything else, so any setting overrides them.
        Dictionary<string, string?> settings = new(defaults ?? new Dictionary<string, string?>())
        {
            // The gallery is hosted with the feed, so clients like Visual Studio link to its package pages.
            ["Feed:PackageDetailsUriTemplate"] = "packages/{id}/{version}",
            ["Logging:LogLevel:Default"] = "Information",
            ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",

            // ASP.NET Core's authentication logs each request without a valid key at Information, in the category of the
            // feed's handler. The feed's own warnings about keys and refused requests stay.
            ["Logging:LogLevel:NuDepot.Feed.Authentication"] = "Warning",
        };
        builder.Configuration.Sources.Insert(0, new MemoryConfigurationSource { InitialData = settings });

        if (!HasAddresses(builder.Configuration))
            builder.WebHost.UseUrls($"http://localhost:{DefaultPort}");

        // Pushes may be as large as Feed:MaxPackageSize, which the feed allows for them. Other requests keep
        // Kestrel's limit.
        return builder;
    }

    private static WebApplicationBuilder Create(string[] args, string contentRoot)
        => WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,

            // A Windows Service starts in C:\Windows\System32, and a console may start anywhere.
            ContentRootPath = contentRoot,
        });

    /// <summary>
    /// The example API keys, commented out, in the nudepot.json the installers start with, which everyone who installs
    /// NuDepot has.
    /// </summary>
    internal static IReadOnlyList<string> ExampleApiKeys { get; } =
    [
        "63ffea38ce443e60fc53b9cfde476b6e4b4188c2667257e2",
        "41d521b060687fec5dc036a747b1423d27672ab0a0d96ebe",
    ];

    // The examples work, so the feed works out of the box, but anyone could use them.
    private static void WarnAboutExampleApiKeys(WebApplication app)
    {
        IEnumerable<IConfigurationSection> keys = app.Configuration
            .GetSection("Feed:Authorization:ApiKeys")
            .GetChildren();
        foreach (IConfigurationSection key in keys.Where(key => key.Value != null && ExampleApiKeys.Contains(key.Value)))
        {
            Log.ExampleApiKey(app.Logger, key.Key);
        }
    }

    /// <summary>
    /// Adds the feed and the gallery, with the optional features, which are off unless their settings turn them on
    /// with <c>IsEnabled</c>: users, signing and mirrors. Each one that's on checks its settings when the service
    /// starts.
    /// </summary>
    public static void AddNuDepot(WebApplicationBuilder builder)
    {
        builder.Services
            .AddNuDepot()
            .AddFeed()
            .EnableUsers()
            .EnableSigning()
            .EnableMirroring()
            .AddGallery();
    }

    /// <summary>
    /// Uses what <see cref="AddNuDepot"/> added. The users' endpoints are only mapped if users are on.
    /// </summary>
    public static void UseNuDepot(WebApplication app)
    {
        WarnAboutExampleApiKeys(app);

        app
            .UseNuDepot()
            .UseFeed()
            .MapUsers()
            .UseGallery();
    }

    // Reads the settings file in place of ASP.NET Core's appsettings.json, from the content root, and in the service,
    // setup.ini under it. The environment's appsettings file, like the project's appsettings.Development.json, stays
    // above them.
    private static void ReadSettingsFrom(IConfigurationBuilder configuration, string folder, bool setup)
    {
        IList<IConfigurationSource> sources = configuration.Sources;
        int appSettings = sources.IndexOf(sources.First(source => source is JsonConfigurationSource { Path: "appsettings.json" }));
        sources.RemoveAt(appSettings);

        PhysicalFileProvider files = new(folder);
        sources.Insert(appSettings, new JsonConfigurationSource
        {
            Path = SettingsFile,
            Optional = true,
            ReloadOnChange = true,
            FileProvider = files,
        });

        if (setup)
        {
            sources.Insert(appSettings, new IniConfigurationSource
            {
                Path = SetupFile,
                Optional = true,
                ReloadOnChange = true,
                FileProvider = files,
            });
        }
    }

    // Whether the settings say where to listen, as URLs, ports or Kestrel endpoints.
    private static bool HasAddresses(IConfiguration configuration)
        => !string.IsNullOrEmpty(configuration["urls"])
        || !string.IsNullOrEmpty(configuration["http_ports"])
        || !string.IsNullOrEmpty(configuration["https_ports"])
        || configuration
            .GetSection("Kestrel:Endpoints")
            .GetChildren()
            .Any();

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = $"The {{Role}} API key is still the example from {SettingsFile}, which everyone who installs NuDepot has. Replace it with a random key of your own.")]
        public static partial void ExampleApiKey(ILogger logger, string role);
    }
}
