using NuDepot.Feed.Entities;

namespace NuDepot.Feed.Search;

/// <summary>
/// A no-op indexer, used when search does not need to index packages.
/// </summary>
internal class NullSearchIndexer : ISearchIndexer
{
    /// <inheritdoc />
    public Task IndexAsync(Package package, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
