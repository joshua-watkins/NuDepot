using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        if (!args.Any(arg => arg is "-h" or "-?" or "--help")) return false;

        Console.WriteLine("""
            A light-weight NuGet service

            Usage: nudepot [command] [options]

            Commands:
              import downloads    Import package downloads from nuget.org.

            Options:
              --urls <urls>       The URLs that NuDepot should bind to.
              --<key>=<value>     Set a configuration value, like --Database:ConnectionString="Data Source=nudepot.db".
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
                using (var cancellation = new CancellationTokenSource())
                using (var scope = host.Services.CreateScope())
                {
                    // Stop the import with Ctrl+C.
                    Console.CancelKeyPress += (_, e) =>
                    {
                        e.Cancel = true;
                        cancellation.Cancel();
                    };

                    var importer = scope.ServiceProvider.GetRequiredService<DownloadsImporter>();

                    await importer.ImportAsync(cancellation.Token);
                }

                return true;

            case var unknown:
                Console.Error.WriteLine($"Unknown command '{string.Join(' ', unknown)}'. Use --help to see the commands.");
                Environment.ExitCode = 1;
                return true;
        }
    }

    // The command is the arguments that aren't options. Options, like --environment Development
    // or --Database:ConnectionString=..., are configuration values that the host already read.
    private static string[] GetCommand(string[] args)
    {
        var command = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith('-') || arg.StartsWith('/'))
            {
                // "--key value" passes the value as the next argument; "--key=value" doesn't.
                if (!arg.Contains('=')) i++;

                continue;
            }

            command.Add(arg);
        }

        return [.. command];
    }
}
