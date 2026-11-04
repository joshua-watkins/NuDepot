namespace NuDepot.Feed.Configuration;

/// <summary>
/// The SQLite database that holds the feed's package information.
/// </summary>
public class DatabaseOptions
{
    /// <summary>
    /// The SQLite connection string, like "Data Source=nudepot.db". Required.
    /// </summary>
    public string? ConnectionString { get; set; }
}
