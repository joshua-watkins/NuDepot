namespace NuDepot.Feed.Endpoints;

/// <summary>
/// One of the signed-in user's signed-in browsers.
/// </summary>
internal class SessionResponse
{
    /// <summary>
    /// The session's ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// When the browser signed in, in UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the browser was last used, in UTC.
    /// </summary>
    public DateTime LastUsedAt { get; set; }

    /// <summary>
    /// Whether it's the browser that asked.
    /// </summary>
    public bool IsCurrent { get; set; }
}
