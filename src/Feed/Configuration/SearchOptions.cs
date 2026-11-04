namespace NuDepot.Feed.Configuration;

/// <summary>
/// How the feed searches packages.
/// </summary>
public class SearchOptions
{
    /// <summary>
    /// What searches query. Defaults to <see cref="SearchType.Database"/>.
    /// </summary>
    public SearchType Type { get; set; } = SearchType.Database;
}

/// <summary>
/// What the feed's searches query.
/// </summary>
public enum SearchType
{
    /// <summary>
    /// Searches query the database.
    /// </summary>
    Database,

    /// <summary>
    /// Searches find nothing.
    /// </summary>
    Null,
}
