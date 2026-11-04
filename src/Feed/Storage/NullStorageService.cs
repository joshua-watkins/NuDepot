namespace NuDepot.Feed.Storage;

/// <summary>
/// A minimal storage implementation, used for advanced scenarios.
/// </summary>
public class NullStorageService : IStorageService
{
    /// <inheritdoc />
    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Stream?> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Stream?>(null);
    }

    /// <inheritdoc />
    public Task<Uri?> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Uri?>(null);
    }

    /// <inheritdoc />
    public Task<StoragePutResult> PutAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(StoragePutResult.Success);
    }
}
