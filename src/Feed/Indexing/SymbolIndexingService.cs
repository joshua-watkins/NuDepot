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
/// <param name="packages">The package database.</param>
/// <param name="storage">Where symbols are stored.</param>
/// <param name="logger">The logger.</param>
public class SymbolIndexingService(IPackageDatabase packages, ISymbolStorageService storage, ILogger<SymbolIndexingService> logger) : ISymbolIndexingService
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

    private IPackageDatabase Packages { get; } = packages;
    private ISymbolStorageService Storage { get; } = storage;
    private ILogger<SymbolIndexingService> Logger { get; } = logger;

    /// <inheritdoc />
    public async Task<SymbolIndexingResult> IndexAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            using (PackageArchiveReader symbolPackage = new(stream, leaveStreamOpen: true))
            {
                IReadOnlyList<string>? pdbPaths = await GetSymbolPackagePdbPathsOrNullAsync(symbolPackage, cancellationToken);
                if (pdbPaths == null)
                    return SymbolIndexingResult.InvalidSymbolPackage;

                // Ensure a corresponding NuGet package exists.
                string packageId = symbolPackage.NuspecReader.GetId();
                NuGetVersion packageVersion = symbolPackage.NuspecReader.GetVersion();

                Package? package = await Packages.FindOrNullAsync(packageId, packageVersion, includeUnlisted: true, cancellationToken);
                if (package == null)
                    return SymbolIndexingResult.PackageNotFound;

                using (PdbList pdbs = new())
                {
                    // Extract the portable PDBs from the snupkg. Nothing is persisted until after all
                    // PDBs have been extracted and validated sucessfully.
                    foreach (string pdbPath in pdbPaths)
                    {
                        PortablePdb? portablePdb = await ExtractPortablePdbAsync(symbolPackage, pdbPath, cancellationToken);
                        if (portablePdb == null)
                            return SymbolIndexingResult.InvalidSymbolPackage;

                        pdbs.Add(portablePdb);
                    }

                    // Persist the portable PDBs to storage.
                    foreach (PortablePdb pdb in pdbs)
                    {
                        await Storage.SavePortablePdbContentAsync(pdb.Filename, pdb.Key, pdb.Content, cancellationToken);
                    }

                    return SymbolIndexingResult.Success;
                }
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Unable to index symbol package due to exception");
            return SymbolIndexingResult.InvalidSymbolPackage;
        }
    }

    private async Task<IReadOnlyList<string>?> GetSymbolPackagePdbPathsOrNullAsync(PackageArchiveReader symbolPackage, CancellationToken cancellationToken)
    {
        try
        {
            await symbolPackage.ValidatePackageEntriesAsync(cancellationToken);

            List<string> files = [.. (await symbolPackage.GetFilesAsync(cancellationToken))];

            // Ensure there are no unexpected file extensions within the symbol package.
            if (!AreSymbolFilesValid(files))
                return null;

            return [.. files.Where(p => Path.GetExtension(p) == ".pdb")];
        }
        catch (Exception)
        {
            // TODO: ValidatePackageEntries throws PackagingException.
            // Add better logging.
            return null;
        }
    }

    private bool AreSymbolFilesValid(IReadOnlyList<string> entries)
    {
        // TODO: Validate that all PDBs are portable. See: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery/Services/SymbolPackageService.cs#L174
        bool IsValidSymbolFileInfo(FileInfo file)
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

    private async Task<PortablePdb?> ExtractPortablePdbAsync(PackageArchiveReader symbolPackage, string pdbPath, CancellationToken cancellationToken)
    {
        // TODO: Validate that the PDB has a corresponding DLL
        // See: https://github.com/NuGet/NuGet.Jobs/blob/master/src/Validation.Symbols/SymbolsValidatorService.cs#L170
        Stream? pdbStream = null;
        PortablePdb? result = null;

        try
        {
            using (Stream rawPdbStream = await symbolPackage.GetStreamAsync(pdbPath, cancellationToken))
            {
                pdbStream = await rawPdbStream.AsTemporaryFileStreamAsync();

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

                pdbStream.Position = 0;
                result = new PortablePdb(fileName, key, pdbStream);
            }
        }
        finally
        {
            if (result == null)
                pdbStream?.Dispose();
        }

        return result;
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
}
