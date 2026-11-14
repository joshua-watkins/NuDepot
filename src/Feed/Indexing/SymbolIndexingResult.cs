namespace NuDepot.Feed.Indexing;

/// <summary>
/// The result of attempting to index a symbol package.
/// See <see cref="ISymbolIndexingService.IndexAsync(Stream, CancellationToken)"/>.
/// </summary>
public enum SymbolIndexingResult
{
    /// <summary>
    /// The symbol package is malformed.
    /// </summary>
    InvalidSymbolPackage,

    /// <summary>
    /// A corresponding package with the provided ID and version does not exist.
    /// </summary>
    PackageNotFound,

    /// <summary>
    /// The feed already has a different PDB with the same key as one of the symbol package's.
    /// </summary>
    SymbolsConflict,

    /// <summary>
    /// The symbol package has been indexed successfully.
    /// </summary>
    Success,
}
