using Microsoft.Extensions.Logging;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Storage;
using NuGet.Packaging;
using NuGet.Versioning;
using System.Reflection.Metadata;

namespace NuDepot.Feed.Indexing;

// Based off: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery/Services/SymbolPackageUploadService.cs
// Based off: https://github.com/NuGet/NuGet.Jobs/blob/master/src/Validation.Symbols/SymbolsValidatorService.cs#L44
/// <summary>
/// Adds pushed symbol packages to the feed, for packages it has.
/// </summary>
/// <param name="logger">The logger.</param>
/// <param name="packages">The package database.</param>
/// <param name="storage">Where symbols are stored.</param>
internal partial class SymbolIndexingService(ILogger<SymbolIndexingService> logger, IPackageDatabase packages, ISymbolStorageService storage) : ISymbolIndexingService
{
    private static readonly HashSet<string> ValidSymbolPackageContentExtensions =
    [
        ".pdb",
        ".nuspec",
        ".xml",
        ".psmdcp",
        ".rels",
        ".p7s"
    ];

    private ILogger Logger { get; } = logger;
    private IPackageDatabase Packages { get; } = packages;
    private ISymbolStorageService Storage { get; } = storage;

    /// <inheritdoc />
    public async Task<SymbolIndexingResult> IndexAsync(Stream stream, CancellationToken cancellationToken)
    {
        // Extract the portable PDBs from the snupkg. Nothing is stored until all of them are extracted and valid.
        using PdbList pdbs = new();
        string packageId;
        NuGetVersion packageVersion;
        try
        {
            using PackageArchiveReader symbolPackage = new(stream, leaveStreamOpen: true);
            IReadOnlyList<string>? pdbPaths = await GetSymbolPackagePdbPathsOrNullAsync(symbolPackage, cancellationToken);
            if (pdbPaths == null)
                return SymbolIndexingResult.InvalidSymbolPackage;

            packageId = symbolPackage.NuspecReader.GetId();
            packageVersion = symbolPackage.NuspecReader.GetVersion();

            foreach (string pdbPath in pdbPaths)
                pdbs.Add(await ExtractPortablePdbAsync(symbolPackage, pdbPath, cancellationToken));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.InvalidSymbolPackage(Logger, e);
            return SymbolIndexingResult.InvalidSymbolPackage;
        }

        // Ensure a corresponding NuGet package exists.
        Package? package = await Packages.FindOrNullAsync(packageId, packageVersion, includeUnlisted: true, cancellationToken);
        if (package == null)
            return SymbolIndexingResult.PackageNotFound;

        // Storage failures aren't the package's fault, so they're the caller's errors.
        foreach (PortablePdb pdb in pdbs)
        {
            if (!await Storage.SavePortablePdbContentAsync(pdb.Filename, pdb.Key, pdb.Content, cancellationToken))
            {
                Log.SymbolsConflict(Logger, packageId, packageVersion, pdb.Filename, pdb.Key);
                return SymbolIndexingResult.SymbolsConflict;
            }
        }

        return SymbolIndexingResult.Success;
    }

    private static async Task<IReadOnlyList<string>?> GetSymbolPackagePdbPathsOrNullAsync(PackageArchiveReader symbolPackage, CancellationToken cancellationToken)
    {
        // Entries can't leave the package's folder when it's extracted.
        await symbolPackage.ValidatePackageEntriesAsync(cancellationToken);

        List<string> files = [.. (await symbolPackage.GetFilesAsync(cancellationToken))];

        // Ensure there are no unexpected file extensions within the symbol package.
        if (!AreSymbolFilesValid(files))
            return null;

        return [.. files.Where(p => Path.GetExtension(p) == ".pdb")];
    }

    private static bool AreSymbolFilesValid(IReadOnlyList<string> entries)
    {
        // TODO: Validate that all PDBs are portable. See: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery/Services/SymbolPackageService.cs#L174
        static bool IsValidSymbolFileInfo(FileInfo file)
        {
            if (string.IsNullOrEmpty(file.Name))
                return false;
            if (string.IsNullOrEmpty(file.Extension))
                return false;
            if (!ValidSymbolPackageContentExtensions.Contains(file.Extension))
                return false;

            return true;
        }

        return entries
            .Select(e => new FileInfo(e))
            .All(IsValidSymbolFileInfo);
    }

    // Throws if the PDB isn't a portable PDB that can be stored.
    private static async Task<PortablePdb> ExtractPortablePdbAsync(PackageArchiveReader symbolPackage, string pdbPath, CancellationToken cancellationToken)
    {
        // TODO: Validate that the PDB has a corresponding DLL
        // See: https://github.com/NuGet/NuGet.Jobs/blob/master/src/Validation.Symbols/SymbolsValidatorService.cs#L170
        Stream? pdbStream = null;
        PortablePdb? result = null;

        try
        {
            using Stream rawPdbStream = await symbolPackage.GetStreamAsync(pdbPath, cancellationToken);
            pdbStream = await rawPdbStream.AsTemporaryFileStreamAsync(cancellationToken);

            string signature;
            using (MetadataReaderProvider pdbReaderProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStream, MetadataStreamOptions.LeaveOpen))
            {
                MetadataReader reader = pdbReaderProvider.GetMetadataReader();
                DebugMetadataHeader header = reader.DebugMetadataHeader
                    ?? throw new InvalidDataException("The symbol file isn't a portable PDB.");
                BlobContentId id = new(header.Id);

                signature = id.Guid
                    .ToString("N")
                    .ToUpperInvariant();
            }

            string fileName = Path
                .GetFileName(pdbPath)
                .ToLowerInvariant();
            string key = $"{signature}ffffffff";
            if (SymbolStorageService.GetPathForKeyOrNull(fileName, key) == null)
                throw new InvalidDataException($"The symbol file name '{fileName}' isn't valid.");

            pdbStream.Position = 0;
            result = new PortablePdb(fileName, key, pdbStream);
        }
        finally
        {
            if (result == null)
                pdbStream?.Dispose();
        }

        return result!;
    }

    private class PortablePdb(string filename, string key, Stream content) : IDisposable
    {
        public string Filename { get; } = filename;
        public string Key { get; } = key;
        public Stream Content { get; } = content;

        public void Dispose() => Content.Dispose();
    }

    private class PdbList : List<PortablePdb>, IDisposable
    {
        public void Dispose()
        {
            foreach (PortablePdb pdb in this)
            {
                pdb.Dispose();
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "The pushed symbol package isn't valid")]
        public static partial void InvalidSymbolPackage(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "The symbol package of {PackageId} {PackageVersion} has {Filename} with the key {Key}, but the feed has a different PDB with that key")]
        public static partial void SymbolsConflict(ILogger logger, string packageId, NuGetVersion packageVersion, string filename, string key);
    }
}
