namespace NuDepot.Feed.Entities;

/// <summary>
/// A signed-in browser. Its cookie has a token that's replaced every few minutes, so a cookie that's used
/// again after the browser used its replacement was copied, and the session is revoked. Only the tokens' hashes are stored.
/// </summary>
internal class UserSession
{
    /// <summary>
    /// The row's key.
    /// </summary>
    public int Key { get; set; }

    /// <summary>
    /// The key of the signed-in user.
    /// </summary>
    public int UserKey { get; set; }

    /// <summary>
    /// The signed-in user.
    /// </summary>
    public User User { get; set; } = null!;

    /// <summary>
    /// The SHA-256 hash of the cookie's current token.
    /// </summary>
    public byte[] TokenHash { get; set; } = null!;

    /// <summary>
    /// The SHA-256 hash of the token it replaced, which is still accepted briefly, for requests the
    /// browser sent at the same time.
    /// </summary>
    public byte[]? PreviousTokenHash { get; set; }

    /// <summary>
    /// When the user signed in, in UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the token was last replaced, in UTC. Tokens are replaced every few minutes while the session is used.
    /// </summary>
    public DateTime RotatedAt { get; set; }

    /// <summary>
    /// Whether the browser has sent the current token. Until it does, the cookie with it may never have arrived, so
    /// the previous token gets a new one instead of revoking the session.
    /// </summary>
    public bool IsTokenUsed { get; set; }

    /// <summary>
    /// When the session expires, in UTC, unless it's used before then.
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}
