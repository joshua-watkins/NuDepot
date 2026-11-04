using NuDepot.Feed;
using NuDepot.Feed.Extensions;
using NuDepot.Gallery.Extensions;

// --port 3333 --proxy, everything else through NuDepot.ini
// maybe "secret" --migrate/upgrade/initialize switch for updating sqlite schema file.

// Runs the NuGet feed server and website as a systemd service.
// When started from a shell instead, it runs as a normal console application.
if (NuDepotCommands.TryWriteHelp(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
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

builder.Services.AddNuDepot(feed =>
{
    feed.AddSqliteDatabase();
    feed.AddFileStorage();
});

// The feed is open until API keys or users are configured. Then each key and user may do what its role allows.
var users = builder.Configuration.HasNuDepotUsers();
if (users || builder.Configuration.HasNuDepotApiKeys())
{
    var authentication = builder.Services.AddAuthentication().AddNuDepotApiKey();
    if (users)
    {
        authentication.AddNuDepotUsers();
    }

    builder.Services.AddAuthorizationBuilder().AddNuDepotPolicies();
}

builder.Services.AddNuDepotUI();

var app = builder.Build();
if (!app.ValidateStartupOptions())
{
    return;
}

// Commands like "import downloads" run instead of the server.
if (await app.TryRunCommandAsync(args))
{
    return;
}

app.UseNuDepot();
app.UseRouting();

app.UseCors(ConfigureNuDepotOptions.CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseOperationCancelledMiddleware();

app.MapNuDepot();
app.MapNuDepotUI();

await app.RunMigrationsAsync();
await app.RunAsync();
