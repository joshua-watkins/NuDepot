using Microsoft.Extensions.Logging;
using NuDepot.Feed.Entities;

namespace NuDepot.Feed.Indexing;

/// <summary>
/// Imports the packages and symbol packages in a folder and its subfolders, for the "import packages" command: a NuGet
/// folder feed, NuGet.Server's Packages folder, another NuDepot's packages folder, or a build's output. Each package
/// is added like a push adds it, except that packages the feed already has are skipped, so an import can run again.
/// Symbol packages are imported after the packages they belong to. Old-style .symbols.nupkg packages are ignored: the
/// feed only serves portable PDBs, from .snupkg packages.
/// </summary>
/// <param name="logger">The logger.</param>
/// <param name="packages">Adds the packages.</param>
/// <param name="symbols">Adds the symbol packages.</param>
internal sealed partial class PackageImporter(ILogger<PackageImporter> logger, PackageIndexingService packages, ISymbolIndexingService symbols)
{
    private ILogger Logger { get; } = logger;
    private PackageIndexingService Packages { get; } = packages;
    private ISymbolIndexingService Symbols { get; } = symbols;

    /// <summary>
    /// Imports the folder's packages, and writes what happened to each, and a summary.
    /// </summary>
    /// <param name="folder">The folder's full path.</param>
    /// <param name="mirrored">Whether the packages are a mirror's, like nuget.org's, rather than the feed's own.</param>
    /// <param name="serviceIndexUrl">The feed's service index URL, which repository signatures record. Needed when signing is on.</param>
    /// <param name="output">Where to write what happened.</param>
    /// <param name="cancellationToken">A token to stop the import.</param>
    /// <returns>Whether every package was added or already there.</returns>
    public async Task<bool> ImportAsync(string folder, bool mirrored, string? serviceIndexUrl, TextWriter output, CancellationToken cancellationToken)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        };
        List<string> files = [.. Directory
            .EnumerateFiles(folder, "*", options)
            .Order(StringComparer.OrdinalIgnoreCase)];
        List<string> packageFiles = [.. files.Where(f => f.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && !IsOldSymbolPackage(f))];
        List<string> symbolFiles = [.. files.Where(f => f.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))];
        int ignored = files.Count(IsOldSymbolPackage);

        int added = 0;
        int existing = 0;
        int failed = 0;
        foreach (string file in packageFiles)
        {
            string name = Path.GetRelativePath(folder, file);
            try
            {
                await using FileStream stream = OpenRead(file);
                (PackageIndexingResult result, Package? package) = await Packages.ImportAsync(stream, mirrored, serviceIndexUrl, cancellationToken);
                switch (result)
                {
                    case PackageIndexingResult.Success:
                        added++;
                        await output.WriteLineAsync($"Added {package!.Id} {package.NormalizedVersionString}");
                        break;

                    case PackageIndexingResult.PackageAlreadyExists:
                        existing++;
                        await output.WriteLineAsync($"Already have {package!.Id} {package.NormalizedVersionString}");
                        break;

                    default:
                        failed++;
                        await output.WriteLineAsync($"Not a valid package: {name}");
                        break;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed++;
                Log.PackageFailed(Logger, e, file);
                await output.WriteLineAsync($"Couldn't import {name}: {e.Message}");
            }
        }

        int symbolsAdded = 0;
        int symbolsFailed = 0;
        foreach (string file in symbolFiles)
        {
            string name = Path.GetRelativePath(folder, file);
            try
            {
                await using FileStream stream = OpenRead(file);
                SymbolIndexingResult result = await Symbols.IndexAsync(stream, cancellationToken);
                if (result == SymbolIndexingResult.Success)
                {
                    symbolsAdded++;
                    await output.WriteLineAsync($"Added symbols {name}");
                    continue;
                }

                symbolsFailed++;
                await output.WriteLineAsync(result switch
                {
                    SymbolIndexingResult.PackageNotFound => $"No package for the symbols {name}",
                    SymbolIndexingResult.SymbolsConflict => $"Other symbols with the same PDBs are already stored: {name}",
                    _ => $"Not a valid symbol package: {name}",
                });
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                symbolsFailed++;
                Log.PackageFailed(Logger, e, file);
                await output.WriteLineAsync($"Couldn't import {name}: {e.Message}");
            }
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync($"Packages: {added} added, {existing} already there, {failed} failed.");
        if (symbolFiles.Count > 0)
            await output.WriteLineAsync($"Symbol packages: {symbolsAdded} added, {symbolsFailed} failed.");
        if (ignored > 0)
            await output.WriteLineAsync($"Ignored {ignored} old-style .symbols.nupkg packages, which the feed doesn't serve.");

        return failed == 0 && symbolsFailed == 0;
    }

    private static bool IsOldSymbolPackage(string file)
        => file.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase);

    private static FileStream OpenRead(string file)
        => new(file, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't import {Path}")]
        public static partial void PackageFailed(ILogger logger, Exception exception, string path);
    }
}
