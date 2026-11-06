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

// The feed reads its settings from the "Feed" section, and the gallery from "Gallery". The settings say whether
// people sign in with passkeys, and create API keys for NuGet clients.
NuDepotService.AddNuDepot(builder);

WebApplication app = builder.Build();

// Commands like "import downloads" run instead of the server.
if (await app.TryRunCommandAsync(args))
    return;

// Without API keys or users, anyone may read, push and unlist. Each key and user may do what its role allows.
NuDepotService.UseNuDepot(app);

await app.RunMigrationsAsync();
await app.RunAsync();
