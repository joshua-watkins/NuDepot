using NuDepot;
using NuDepot.Feed;

// Runs the NuGet feed server and website as a Windows Service.
// When started from a console instead, it runs as a normal console application.
if (NuDepotCommands.TryWriteHelp(args))
    return;

// The settings, database and packages are in C:\ProgramData\NuDepot, which is the working directory.
string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NuDepot");
WebApplicationBuilder builder = NuDepotService.CreateBuilder(args, folder);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "NuDepot";
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
