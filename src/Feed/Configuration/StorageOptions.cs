namespace NuDepot.Feed.Configuration;

/// <summary>
/// Where the feed stores package and symbol files.
/// </summary>
public class StorageOptions
{
    public StorageType Type { get; set; } = StorageType.FileSystem;

    /// <summary>
    /// The folder <see cref="StorageType.FileSystem"/> storage keeps files in. Defaults to the
    /// working directory. The folder is created if it doesn't exist. Packages are stored in a
    /// subfolder named "packages".
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Store content in a new folder in the system's temporary folder instead of <see cref="Path"/>,
    /// and delete it when the application stops. For development, with an in-memory database.
    /// </summary>
    public bool Temporary { get; set; }
}

public enum StorageType
{
    /// <summary>
    /// Files are stored in a folder, <see cref="StorageOptions.Path"/>.
    /// </summary>
    FileSystem,

    /// <summary>
    /// Files aren't stored.
    /// </summary>
    Null,
}
