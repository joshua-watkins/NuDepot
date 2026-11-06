namespace NuDepot.Feed.Configuration;

/// <summary>
/// The SQLite database that holds the feed's package information.
/// </summary>
public class DatabaseOptions
{
    /// <summary>
    /// The SQLite connection string. Defaults to "Data Source=nudepot.db", in the working directory.
    /// </summary>
    public string? ConnectionString { get; set; } = "Data Source=nudepot.db";
}
