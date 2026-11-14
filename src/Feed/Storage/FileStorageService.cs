using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Extensions;

namespace NuDepot.Feed.Storage;

/// <summary>
/// Stores content on disk.
/// </summary>
internal class FileStorageService : IStorageService
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

        // Sharing deletes lets ReplaceAsync replace the file while it's being read: readers keep the old file.
        path = GetFullPath(path);
        FileStream content = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

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
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrEmpty(contentType))
            throw new ArgumentException("Content type is required", nameof(contentType));

        cancellationToken.ThrowIfCancellationRequested();

        path = GetFullPath(path);

        // Ensure that the path exists.
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // The content is written next to its path and then moved there, so a write that fails part way, like a
        // canceled push, doesn't leave part of a file that later pushes would conflict with.
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream fileStream = File.Open(temporaryPath, FileMode.CreateNew))
            {
                await content.CopyToAsync(fileStream, DefaultCopyBufferSize, cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: false);
            return StoragePutResult.Success;
        }
        catch (IOException) when (File.Exists(path))
        {
            using FileStream targetStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            content.Position = 0;
            return content.Matches(targetStream)
                ? StoragePutResult.AlreadyExists
                : StoragePutResult.Conflict;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    /// <summary>
    /// Replace content on disk: the new content is written to a temporary file next to it, which then replaces it.
    /// Readers that already opened the file keep reading the old content.
    /// </summary>
    /// <param name="path">The path at which to store the content.</param>
    /// <param name="content">The new content.</param>
    /// <param name="contentType">The type of content that is being stored.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>A task that completes when the new content is stored.</returns>
    public async Task ReplaceAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        path = GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream fileStream = File.Open(temporaryPath, FileMode.CreateNew))
            {
                await content.CopyToAsync(fileStream, DefaultCopyBufferSize, cancellationToken);
            }

            if (OperatingSystem.IsWindows())
                ReplaceOpenFile(temporaryPath, path);
            else
                File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    // Windows can't replace a file that readers have open, even though they share deletes, but it can rename it.
    // So the old file is moved aside and deleted, which happens once its readers close it.
    private static void ReplaceOpenFile(string temporaryPath, string path)
    {
        string oldPath = $"{path}.{Guid.NewGuid():N}.old";
        bool movedAside = false;
        try
        {
            File.Move(path, oldPath);
            movedAside = true;
        }
        catch (FileNotFoundException)
        {
        }

        try
        {
            File.Move(temporaryPath, path);
        }
        catch when (movedAside)
        {
            File.Move(oldPath, path);
            throw;
        }

        if (movedAside)
            File.Delete(oldPath);
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
