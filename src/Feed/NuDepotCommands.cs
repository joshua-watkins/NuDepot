using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
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
        using CancellationTokenSource cancellation = new();

        // Stop the import with Ctrl+C.
        ConsoleCancelEventHandler cancel = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        Console.CancelKeyPress += cancel;
        try
        {
            using IServiceScope scope = host.Services.CreateScope();
            DownloadsImporter importer = scope.ServiceProvider.GetRequiredService<DownloadsImporter>();

            await importer.ImportAsync(cancellation.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
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
