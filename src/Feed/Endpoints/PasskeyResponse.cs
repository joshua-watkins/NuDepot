namespace NuDepot.Feed.Endpoints;

/// <summary>
/// One of the signed-in user's passkeys.
/// </summary>
internal class PasskeyResponse
{
    /// <summary>
    /// The passkey's ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The name the user gave the passkey.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// When the passkey was created, in UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the passkey was last used to sign in, in UTC.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Whether the passkey is synced, for example by a password manager.
    /// </summary>
    public bool IsBackedUp { get; set; }
}
