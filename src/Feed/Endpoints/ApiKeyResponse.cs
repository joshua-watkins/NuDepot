namespace NuDepot.Feed.Endpoints;

/// <summary>
/// One of the signed-in user's API keys.
/// </summary>
internal class ApiKeyResponse
{
    /// <summary>
    /// The key's ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The name the user gave the key.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// The start of the key, to recognize it.
    /// </summary>
    public string Prefix { get; set; } = null!;

    /// <summary>
    /// When the key was created, in UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the key was last used, in UTC.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// The key itself. Only returned when it's created.
    /// </summary>
    public string? Key { get; set; }
}
