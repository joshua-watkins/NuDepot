namespace NuDepot.Feed.Entities;

/// <summary>
/// A key that signs the users' sign-in cookies, which are JWTs. The feed generates one when it first needs it. Its
/// private key is encrypted by ASP.NET Core data protection, like generated signing certificates' are.
/// </summary>
public class SessionSigningKey
{
    /// <summary>
    /// The row's key.
    /// </summary>
    public int Key { get; set; }

    /// <summary>
    /// The JWT key ID ("kid") of the tokens it signs. Unique.
    /// </summary>
    public string KeyId { get; set; } = null!;

    /// <summary>
    /// The ECDSA P-256 private key, as PKCS #8, encrypted with ASP.NET Core data protection.
    /// </summary>
    public byte[] ProtectedPrivateKey { get; set; } = null!;

    /// <summary>
    /// When the key was generated, in UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
