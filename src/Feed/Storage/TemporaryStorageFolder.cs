namespace NuDepot.Feed.Storage;

/// <summary>
/// The folder that file storage uses when <see cref="Configuration.StorageOptions.Temporary"/> is set.
/// Each run of the application gets a new folder, which is deleted when the application stops.
/// </summary>
internal sealed class TemporaryStorageFolder : IDisposable
{
    private Lazy<string> LazyPath { get; } = new(() =>
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NuDepot", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    });

    /// <summary>
    /// The folder's path. It's created the first time it's used.
    /// </summary>
    public string Path => LazyPath.Value;

    /// <inheritdoc />
    public void Dispose()
    {
        if (!LazyPath.IsValueCreated)
            return;

        try
        {
            Directory.Delete(LazyPath.Value, recursive: true);
        }
        catch (IOException)
        {
            // A file is still open. The system cleans up its temporary folder eventually.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
