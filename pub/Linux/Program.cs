using NuDepot;
using NuDepot.Feed;

// Runs the NuGet feed server and website as a systemd service.
// When started from a shell instead, it runs as a normal console application.
if (NuDepotCommands.TryWriteHelp(args))
    return;

// The settings are in /etc/nudepot, which is the working directory. The database and packages are in
// /var/lib/nudepot, which systemd creates for the service (StateDirectory= in nudepot.service).
WebApplicationBuilder builder = NuDepotService.CreateBuilder(args, "/etc/nudepot", new Dictionary<string, string?>
{
    ["Feed:Database:ConnectionString"] = "Data Source=/var/lib/nudepot/nudepot.db",
    ["Feed:Storage:Path"] = "/var/lib/nudepot",
});

// Notifies systemd when the service has started and is stopping (Type=notify),
// and formats console logs for the journal.
builder.Services.AddSystemd();

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
