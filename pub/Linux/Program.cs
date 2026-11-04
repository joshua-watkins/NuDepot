using NuDepot.Feed;

// --port 3333 --proxy, everything else through NuDepot.ini
// maybe "secret" --migrate/upgrade/initialize switch for updating sqlite schema file.

// Runs the NuGet feed server and website as a systemd service.
// When started from a shell instead, it runs as a normal console application.
if (NuDepotCommands.TryWriteHelp(args))
    return;

WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,

    // Read appsettings.json and the website's static files from the application's folder,
    // wherever the service or shell started it from.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.UseNuDepotConfigRoot();

// Notifies systemd when the service has started and is stopping (Type=notify),
// and formats console logs for the journal.
builder.Services.AddSystemd();

builder.WebHost.ConfigureKestrel(options =>
{
    // Remove the upload limit from Kestrel. If needed, an upload limit can
    // be enforced by a reverse proxy server, like nginx.
    options.Limits.MaxRequestBodySize = null;
});

// The feed reads its settings from the "Feed" section, and the gallery from "Gallery".
builder.Services
    .AddNuDepot()
    .AddFeed()
    .AddGallery();

WebApplication app = builder.Build();

// Commands like "import downloads" run instead of the server.
if (await app.TryRunCommandAsync(args))
    return;

// The feed is open until API keys or users are configured. Then each key and user may do what its role allows.
app
    .UseNuDepot()
    .UseFeed()
    .UseApiKeys()
    .UsePasskeys()
    .UseGallery();

await app.RunMigrationsAsync();
await app.RunAsync();
