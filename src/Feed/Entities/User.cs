using NuDepot.Feed.Authentication;
using NuDepot.Feed.Users;

namespace NuDepot.Feed.Entities;

/// <summary>
/// A person who signs in to the feed with a passkey. Until their first passkey is created, the
/// account is only a sign-up in progress.
/// </summary>
public class User
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

/// <summary>
/// A signed-in browser. Its cookie has a token that's replaced every few minutes, so a cookie that's used
/// again after the browser used its replacement was copied, and the session is revoked. Only the tokens' hashes are stored.
/// </summary>
public class UserSession
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

/// <summary>
/// A passkey a user signs in with: the public key of a WebAuthn credential.
/// </summary>
public class UserPasskey
{
    /// <summary>
    /// The row's key.
    /// </summary>
    public int Key { get; set; }

    /// <summary>
    /// The key of the user the passkey belongs to.
    /// </summary>
    public int UserKey { get; set; }

    /// <summary>
    /// The user the passkey belongs to.
    /// </summary>
    public User User { get; set; } = null!;

    /// <summary>
    /// The WebAuthn credential ID. Unique.
    /// </summary>
    public byte[] CredentialId { get; set; } = null!;

    /// <summary>
    /// The credential's public key, COSE encoded.
    /// </summary>
    public byte[] PublicKey { get; set; } = null!;

    /// <summary>
    /// The authenticator's signature counter, which helps detect cloned authenticators.
    /// </summary>
    public uint SignCount { get; set; }

    /// <summary>
    /// How the browser can reach the authenticator, like "internal" or "usb".
    /// </summary>
    public string[]? Transports { get; set; }

    /// <summary>
    /// The authenticator's model.
    /// </summary>
    public Guid AaGuid { get; set; }

    /// <summary>
    /// Whether the passkey is synced, for example by a password manager.
    /// </summary>
    public bool IsBackedUp { get; set; }

    /// <summary>
    /// A name the user gives the passkey, to tell their passkeys apart.
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
}

/// <summary>
/// An API key a user created for NuGet clients. Only its hash is stored.
/// </summary>
public class UserApiKey
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
