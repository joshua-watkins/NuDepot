namespace NuDepot.Feed.Indexing;

/// <summary>
/// The service used to accept new symbol packages.
/// </summary>
public interface ISymbolIndexingService
{
    /// <summary>
    /// Attempt to index a new symbol package.
    /// </summary>
    /// <param name="stream">The stream containing the symbol package's content.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The result of the attempted indexing operation.</returns>
    Task<SymbolIndexingResult> IndexAsync(Stream stream, CancellationToken cancellationToken);
}
