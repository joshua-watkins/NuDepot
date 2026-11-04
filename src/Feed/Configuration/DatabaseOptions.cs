namespace NuDepot.Feed.Configuration;

public class DatabaseOptions
{
    public string? Type { get; set; }

    /// <summary>
    /// Required.
    /// </summary>
    public string? ConnectionString { get; set; }
}
