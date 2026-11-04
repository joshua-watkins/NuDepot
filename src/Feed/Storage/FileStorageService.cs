using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Extensions;

namespace NuDepot.Feed.Storage;

/// <summary>
/// Stores content on disk.
/// </summary>
public class FileStorageService : IStorageService
{
    // See: https://github.com/dotnet/corefx/blob/master/src/Common/src/CoreLib/System/IO/Stream.cs#L35
    private const int DefaultCopyBufferSize = 81920;

    private string StorePath { get; }

    /// <summary>
    /// Stores content in <see cref="Configuration.StorageOptions.Path"/>.
    /// </summary>
    /// <param name="options">The feed's options.</param>
    public FileStorageService(IOptionsSnapshot<FeedOptions> options)
    {
        // Resolve relative path components ('.'/'..') and ensure there is a trailing slash.
        StorePath = Path.GetFullPath(options.Value.Storage.Path ?? Directory.GetCurrentDirectory());
        if (!StorePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            StorePath += Path.DirectorySeparatorChar;
    }

    /// <inheritdoc />
    public Task<Stream?> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        path = GetFullPath(path);
        FileStream content = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        return Task.FromResult<Stream?>(content);
    }

    /// <inheritdoc />
    public Task<Uri?> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Uri result = new(GetFullPath(path));

        return Task.FromResult<Uri?>(result);
    }

    /// <inheritdoc />
    public async Task<StoragePutResult> PutAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));
        if (string.IsNullOrEmpty(contentType))
            throw new ArgumentException("Content type is required", nameof(contentType));

        cancellationToken.ThrowIfCancellationRequested();

        path = GetFullPath(path);

        // Ensure that the path exists.
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            using (FileStream fileStream = File.Open(path, FileMode.CreateNew))
            {
                await content.CopyToAsync(fileStream, DefaultCopyBufferSize, cancellationToken);
                return StoragePutResult.Success;
            }
        }
        catch (IOException) when (File.Exists(path))
        {
            using (FileStream targetStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                content.Position = 0;
                return content.Matches(targetStream)
                    ? StoragePutResult.AlreadyExists
                    : StoragePutResult.Conflict;
            }
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            File.Delete(GetFullPath(path));
        }
        catch (DirectoryNotFoundException)
        {
        }

        return Task.CompletedTask;
    }

    private string GetFullPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path is required", nameof(path));

        string fullPath = Path.GetFullPath(Path.Combine(StorePath, path));

        // Verify path is under the _storePath.
        if (!fullPath.StartsWith(StorePath, StringComparison.Ordinal) ||
            fullPath.Length == StorePath.Length)
            throw new ArgumentException("Path resolves outside store path", nameof(path));

        return fullPath;
    }
}
