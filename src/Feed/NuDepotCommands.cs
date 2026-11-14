using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Indexing;
using NuDepot.Feed.Signing;
using NuDepot.Feed.Upstream;

namespace NuDepot.Feed;

/// <summary>
/// The command line commands that hosts of the server support, besides running the server.
/// </summary>
public static class NuDepotCommands
{
    /// <summary>
    /// Writes the command line help if the arguments ask for it. Call this before building the host,
    /// as the host's configuration can't read "--help" as a setting.
    /// </summary>
    /// <returns>Whether the help was written, and the application should exit.</returns>
    public static bool TryWriteHelp(string[] args)
    {
        if (!args.Any(arg => arg is "-h" or "-?" or "--help"))
            return false;

        Console.WriteLine("""
            A light-weight NuGet service

            Usage: nudepot [command] [options]

            Commands:
              import downloads                Import package downloads from nuget.org.
              import packages <folder>        Add the packages (.nupkg) and symbol packages (.snupkg) in a folder
                                              and its subfolders, like a NuGet folder feed or NuGet.Server's
                                              Packages folder. Give the folder's full path. Packages the feed
                                              already has are skipped.
                --mirrored                    Add them as mirrored packages, like a copy of nuget.org's, so the
                                              mirrors still provide their other versions.
                --service-index <url>         The feed's service index URL, like
                                              https://nuget.example.com/v3/index.json. Needed when the feed signs
                                              packages, since their signatures record it.
              rotate signing-certificate      Replace this year's generated signing certificate, like after its
                                              key leaked. The old one is revoked, and packages it signed are
                                              re-signed when they're downloaded. A running server uses the new
                                              one within a minute.

            Options:
              --urls <urls>       The URLs that NuDepot should bind to.
              --<key>=<value>     Set a configuration value, like --Feed:Database:ConnectionString="Data Source=nudepot.db".
              -h|--help           Show help information.

            With no command, runs the server.
            """);
        return true;
    }

    /// <summary>
    /// Runs the command in the arguments, if there is one.
    /// </summary>
    /// <returns>Whether the arguments had a command, and the application should exit instead of running the server.</returns>
    public static async Task<bool> TryRunCommandAsync(this IHost host, string[] args)
    {
        switch (GetCommand(args))
        {
            case []:
                return false;

            case ["import", "downloads"]:
                await ImportDownloadsAsync(host);
                return true;

            // The folder may look like an option, like /var/packages, so it's read from the arguments themselves.
            case ["import", "packages", ..]:
                await ImportPackagesAsync(host, args);
                return true;

            case ["rotate", "signing-certificate"]:
                await RotateSigningCertificateAsync(host);
                return true;

            case var unknown:
                Console.Error.WriteLine($"Unknown command '{string.Join(' ', unknown)}'. Use --help to see the commands.");
                Environment.ExitCode = 1;
                return true;
        }
    }

    private static async Task ImportDownloadsAsync(IHost host)
    {
        await RunUntilCancelledAsync(async cancellationToken =>
        {
            using IServiceScope scope = host.Services.CreateScope();
            DownloadsImporter importer = scope.ServiceProvider.GetRequiredService<DownloadsImporter>();

            await importer.ImportAsync(cancellationToken);
        });
    }

    private const string MirroredOption = "--mirrored";
    private const string ServiceIndexOption = "--service-index";

    private static async Task ImportPackagesAsync(IHost host, string[] args)
    {
        (string? folder, bool mirrored, string? serviceIndexUrl) = ParseImportPackages(args);
        string? error = folder switch
        {
            null => "Give the folder to import, like: nudepot import packages C:\\packages",
            _ when !Path.IsPathFullyQualified(folder) => $"Give the folder's full path, rather than '{folder}': NuDepot runs in its settings folder.",
            _ when !Directory.Exists(folder) => $"The folder '{folder}' doesn't exist, or NuDepot can't read it.",
            _ => null,
        };

        bool signs = host.Services.GetRequiredService<IOptions<RepositorySigningOptions>>().Value.IsEnabled;
        if (error == null && serviceIndexUrl != null &&
            !(Uri.TryCreate(serviceIndexUrl, UriKind.Absolute, out Uri? url) && url.Scheme == Uri.UriSchemeHttps))
            error = $"'{serviceIndexUrl}' isn't the feed's HTTPS service index URL, like https://nuget.example.com/v3/index.json.";
        else if (error == null && signs && serviceIndexUrl == null)
            error = $"The feed signs packages, and their signatures record its service index URL. Give it with {ServiceIndexOption}, like {ServiceIndexOption} https://nuget.example.com/v3/index.json.";

        if (error != null)
        {
            Console.Error.WriteLine(error);
            Environment.ExitCode = 1;
            return;
        }

        await host.RunMigrationsAsync();
        await RunUntilCancelledAsync(async cancellationToken =>
        {
            using IServiceScope scope = host.Services.CreateScope();
            PackageImporter importer = scope.ServiceProvider.GetRequiredService<PackageImporter>();

            if (!await importer.ImportAsync(Path.GetFullPath(folder!), mirrored, serviceIndexUrl, Console.Out, cancellationToken))
                Environment.ExitCode = 1;
        });
    }

    // The folder is the first argument after "import packages" that isn't one of its options or their values.
    private static (string? Folder, bool Mirrored, string? ServiceIndexUrl) ParseImportPackages(string[] args)
    {
        string? folder = null;
        bool mirrored = false;
        string? serviceIndexUrl = null;

        int start = Array.IndexOf(args, "packages", Array.IndexOf(args, "import") + 1) + 1;
        for (int i = start; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == MirroredOption)
                mirrored = true;
            else if (arg == ServiceIndexOption && i + 1 < args.Length)
                serviceIndexUrl = args[++i];
            else if (arg.StartsWith(ServiceIndexOption + "=", StringComparison.Ordinal))
                serviceIndexUrl = arg[(ServiceIndexOption.Length + 1)..];
            else if (arg.StartsWith("--", StringComparison.Ordinal))
                i += arg.Contains('=') ? 0 : 1;
            else
                folder ??= arg;
        }

        return (folder, mirrored, serviceIndexUrl);
    }

    // Runs a command that Ctrl+C stops.
    private static async Task RunUntilCancelledAsync(Func<CancellationToken, Task> command)
    {
        using CancellationTokenSource cancellation = new();

        void Cancel(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cancellation.Cancel();
        }

        Console.CancelKeyPress += Cancel;
        try
        {
            await command(cancellation.Token);
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
        }
    }

    private static async Task RotateSigningCertificateAsync(IHost host)
    {
        RepositorySigningOptions options = host.Services.GetRequiredService<IOptions<RepositorySigningOptions>>().Value;
        if (!options.GeneratesCertificates)
        {
            Console.Error.WriteLine(
                "Only generated certificates can be rotated: signing must be on, without a certificate. In the NuDepot " +
                $"service, set '{RepositorySigningOptions.Section}:{nameof(RepositorySigningOptions.IsEnabled)}' to true.");
            Environment.ExitCode = 1;
            return;
        }

        await host.RunMigrationsAsync();
        SigningCertificateRotation rotation = await host.Services.GetRequiredService<GeneratedSigningCertificates>().RotateAsync(CancellationToken.None);

        Console.WriteLine($"New signing certificate: {rotation.Fingerprint}, which expires at {rotation.ExpiresAt:u}.");
        foreach (string revoked in rotation.Revoked)
            Console.WriteLine($"Revoked: {revoked}. Remove it from your NuGet trusted signers.");
    }

    // The command is the arguments that aren't options. Options, like --environment Development
    // or --Database:ConnectionString=..., are configuration values that the host already read.
    private static string[] GetCommand(string[] args)
    {
        List<string> command = [];

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith('-') || arg.StartsWith('/'))
            {
                // "--key value" passes the value as the next argument; "--key=value" doesn't.
                if (!arg.Contains('='))
                    i++;

                continue;
            }

            command.Add(arg);
        }

        return [.. command];
    }
}
