namespace NuDepot.Feed.Entities;

/// <summary>
/// A passkey a user signs in with: the public key of a WebAuthn credential.
/// </summary>
internal class UserPasskey
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
