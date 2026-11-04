using NuDepot.Feed;
using NuDepot.Feed.Extensions;
using NuDepot.Gallery.Extensions;

// Runs the NuGet feed server and website as a Windows Service.
// When started from a console instead, it runs as a normal console application.
if (NuDepotCommands.TryWriteHelp(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,

    // A Windows Service starts in C:\Windows\System32, and a console may start anywhere.
    // Always read appsettings.json and the website's static files from the application's folder.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.UseNuDepotConfigRoot();

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "NuDepot";
});

builder.WebHost.ConfigureKestrel(options =>
{
    // Remove the upload limit from Kestrel. If needed, an upload limit can
    // be enforced by a reverse proxy server, like IIS.
    options.Limits.MaxRequestBodySize = null;
});

builder.Services.AddNuDepot(feed =>
{
    // Use SQLite as the database and store packages on the local file system.
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
