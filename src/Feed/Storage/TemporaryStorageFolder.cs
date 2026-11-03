using System;
using System.IO;

namespace NuGet.Feed.Storage;

/// <summary>
/// The folder that file storage uses when <see cref="FileSystemStorageOptions.Temporary"/> is set.
/// Each run of the application gets a new folder, which is deleted when the application stops.
/// </summary>
public sealed class TemporaryStorageFolder : IDisposable
{
    private readonly Lazy<string> _path = new(() =>
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NuGetFeed", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    });

    public string Path => _path.Value;

    public void Dispose()
    {
        if (!_path.IsValueCreated) return;

        try
        {
            Directory.Delete(_path.Value, recursive: true);
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
