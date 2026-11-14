namespace NuDepot.Feed.Entities;

/// <summary>
/// An API key a user created for NuGet clients. Only its hash is stored.
/// </summary>
internal class UserApiKey
{
    /// <summary>
    /// The row's key.
    /// </summary>
    public int Key { get; set; }

    /// <summary>
    /// The key of the user the API key belongs to.
    /// </summary>
    public int UserKey { get; set; }

    /// <summary>
    /// The user the API key belongs to.
    /// </summary>
    public User User { get; set; } = null!;

    /// <summary>
    /// A name the user gives the key, like "Build server".
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// The start of the key, so the user can recognize it.
    /// </summary>
    public string Prefix { get; set; } = null!;

    /// <summary>
    /// The SHA-256 hash of the key. Unique.
    /// </summary>
    public byte[] Hash { get; set; } = null!;

    /// <summary>
    /// When the key was created, in UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the key was last used, in UTC.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }
}
