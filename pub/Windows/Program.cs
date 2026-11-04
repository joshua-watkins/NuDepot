using NuDepot.Feed;

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

// The feed reads its settings from the "Feed" section, and the gallery from "Gallery".
builder.Services.AddNuDepot()
    .AddFeed()
    .AddGallery();

var app = builder.Build();

// Commands like "import downloads" run instead of the server.
if (await app.TryRunCommandAsync(args))
{
    return;
}

// The feed is open until API keys or users are configured. Then each key and user may do what its role allows.
app.UseNuDepot()
    .UseFeed()
    .UseApiKeys()
    .UsePasskeys()
    .UseGallery();

await app.RunMigrationsAsync();
await app.RunAsync();
