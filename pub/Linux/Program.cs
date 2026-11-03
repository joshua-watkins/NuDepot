using NuGet.Feed;
using NuGet.Feed.Extensions;
using NuGet.Gallery.Extensions;

// --port 3333 --proxy, everything else through NuGetFeed.ini
// maybe "secret" --migrate/upgrade/initialize switch for updating sqlite schema file.

// Runs the NuGet feed server and website as a systemd service.
// When started from a shell instead, it runs as a normal console application.
if (NuGetFeedCommands.TryWriteHelp(args))
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

builder.UseNuGetFeedConfigRoot();

// Notifies systemd when the service has started and is stopping (Type=notify),
// and formats console logs for the journal.
builder.Services.AddSystemd();

builder.WebHost.ConfigureKestrel(options =>
{
    // Remove the upload limit from Kestrel. If needed, an upload limit can
    // be enforced by a reverse proxy server, like nginx.
    options.Limits.MaxRequestBodySize = null;
});

builder.Services.AddNuGetFeed(feed =>
{
    feed.AddSqliteDatabase();
    feed.AddFileStorage();
});

// The feed is open until API keys or users are configured. Then each key and user may do what its role allows.
var users = builder.Configuration.HasNuGetFeedUsers();
if (users || builder.Configuration.HasNuGetFeedApiKeys())
{
    var authentication = builder.Services.AddAuthentication().AddNuGetFeedApiKey();
    if (users)
    {
        authentication.AddNuGetFeedUsers();
    }

    builder.Services.AddAuthorizationBuilder().AddNuGetFeedPolicies();
}

builder.Services.AddNuGetFeedUI();

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

app.UseNuGetFeed();
app.UseRouting();

app.UseCors(ConfigureNuGetFeedOptions.CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseOperationCancelledMiddleware();

app.MapNuGetFeed();
app.MapNuGetFeedUI();

await app.RunMigrationsAsync();
await app.RunAsync();
