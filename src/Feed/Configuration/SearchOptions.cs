namespace NuDepot.Feed.Configuration;

/// <summary>
/// How the feed searches packages.
/// </summary>
public class SearchOptions
{
    public SearchType Type { get; set; } = SearchType.Database;
}

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
