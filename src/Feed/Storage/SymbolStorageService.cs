namespace NuDepot.Feed.Storage;

/// <summary>
/// Stores symbol files, by the key the symbol server looks them up by.
/// </summary>
/// <param name="storage">Where files are stored.</param>
public class SymbolStorageService(IStorageService storage) : ISymbolStorageService
{
    private const string SymbolsPathPrefix = "symbols";
    private const string PdbContentType = "binary/octet-stream";

    private IStorageService Storage { get; } = storage;

    /// <inheritdoc />
    public async Task<bool> SavePortablePdbContentAsync(string filename, string key, Stream pdbStream, CancellationToken cancellationToken)
    {
        string path = GetPathForKeyOrNull(filename, key)
            ?? throw new ArgumentException($"The PDB {filename} with the key {key} can't be stored: its name or key isn't valid.");
        StoragePutResult result = await Storage.PutAsync(path, pdbStream, PdbContentType, cancellationToken);

        return result != StoragePutResult.Conflict;
    }

    /// <inheritdoc />
    public async Task<Stream?> GetPortablePdbContentStreamOrNullAsync(string filename, string key)
    {
        // A debugger asking for a file that can't be on the feed gets "not found", like for one that isn't.
        string? path = GetPathForKeyOrNull(filename, key);
        if (path == null)
            return null;

        try
        {
            return await Storage.GetAsync(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Where a PDB is stored, or null if its name could leave its folder, or its key isn't a portable PDB's.
    /// </summary>
    internal static string? GetPathForKeyOrNull(string filename, string key)
    {
        if (string.IsNullOrEmpty(filename) ||
            filename is "." or ".." ||
            filename.IndexOfAny(['/', '\\']) >= 0 ||
            filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;

        // The key's first 32 characters are the GUID, the remaining characters are the age.
        // See: https://github.com/dotnet/symstore/blob/98717c63ec8342acf8a07aa5c909b88bd0c664cc/docs/specs/SSQP_Key_Conventions.md#portable-pdb-signature
        if (key.Length < 32 || !key.All(char.IsAsciiLetterOrDigit))
            return null;

        // Debuggers should always use the age "ffffffff", however Visual Studio 2019
        // users have reported other age values. We will ignore the age.
        key = key[..32] + "ffffffff";

        return Path.Combine(
            SymbolsPathPrefix,
            filename.ToLowerInvariant(),
            key.ToLowerInvariant());
    }
}
