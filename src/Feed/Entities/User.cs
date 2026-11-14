using NuDepot.Feed.Authentication;
using NuDepot.Feed.Users;

namespace NuDepot.Feed.Entities;

/// <summary>
/// A person who signs in to the feed with a passkey. Until their first passkey is created, the
/// account is only a sign-up in progress.
/// </summary>
internal class User
{
    /// <summary>
    /// The row's key.
    /// </summary>
    public int Key { get; set; }

    /// <summary>
    /// The name the user signs up with. Unique, ignoring case.
    /// </summary>
    public string UserName { get; set; } = null!;

    /// <summary>
    /// What the user, and their API keys, may do.
    /// </summary>
    public FeedRole Role { get; set; }

    /// <summary>
    /// The random WebAuthn user handle that passkeys store, instead of anything that identifies the user.
    /// </summary>
    public byte[] Handle { get; set; } = null!;

    /// <summary>
    /// When the account was created, in UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The passkey ceremony in progress, if any. Starting another replaces it, so each user has at most one,
    /// and answering it clears it, so each challenge is only answered once.
    /// </summary>
    public PasskeyCeremony? Ceremony { get; set; }

    /// <summary>
    /// The ceremony's options sent to the browser, as JSON, including the challenge.
    /// </summary>
    public string? CeremonyOptions { get; set; }

    /// <summary>
    /// The name of the passkey being created.
    /// </summary>
    public string? CeremonyPasskeyName { get; set; }

    /// <summary>
    /// When the passkey ceremony in progress expires, in UTC.
    /// </summary>
    public DateTime? CeremonyExpiresAt { get; set; }

    /// <summary>
    /// The user's passkeys.
    /// </summary>
    public List<UserPasskey> Passkeys { get; set; } = [];

    /// <summary>
    /// The user's API keys.
    /// </summary>
    public List<UserApiKey> ApiKeys { get; set; } = [];

    /// <summary>
    /// The user's signed-in browsers.
    /// </summary>
    public List<UserSession> Sessions { get; set; } = [];
}
