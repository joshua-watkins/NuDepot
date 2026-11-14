namespace NuDepot.Feed.Endpoints;

/// <summary>
/// A user, for the administration page.
/// </summary>
internal class AdminUserResponse
{
    /// <summary>
    /// The user's ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The user's name.
    /// </summary>
    public string UserName { get; set; } = null!;

    /// <summary>
    /// The user's role: "ReadOnly", "ReadWrite" or "Admin".
    /// </summary>
    public string Role { get; set; } = null!;

    /// <summary>
    /// When the account was created, in UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the user last signed in with a passkey, in UTC.
    /// </summary>
    public DateTime? LastSignedInAt { get; set; }

    /// <summary>
    /// How many passkeys the user has.
    /// </summary>
    public int Passkeys { get; set; }

    /// <summary>
    /// How many API keys the user has.
    /// </summary>
    public int ApiKeys { get; set; }

    /// <summary>
    /// How many browsers the user is signed in on.
    /// </summary>
    public int Sessions { get; set; }

    /// <summary>
    /// Whether it's the user who asked.
    /// </summary>
    public bool IsCurrentUser { get; set; }
}
