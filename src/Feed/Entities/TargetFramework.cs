namespace NuDepot.Feed.Entities;

/// <summary>
/// A framework a package version targets.
/// </summary>
public class TargetFramework
{
    /// <summary>
    /// The row's key.
    /// </summary>
    public int Key { get; set; }

    /// <summary>
    /// The framework's short name, like "net10.0".
    /// </summary>
    public string? Moniker { get; set; }

    /// <summary>
    /// The package version that targets the framework.
    /// </summary>
    public Package Package { get; set; } = null!;
}
