namespace NuDepot.Feed.Entities;

/// <summary>
/// A repository signing certificate the feed generated, with
/// <see cref="Configuration.RepositorySigningOptions.GenerateCertificates"/>. Each year has its own, which is
/// generated when the feed first needs it that year, and expires at the end of the year. A certificate replaced
/// early, like after its key leaked, is revoked.
/// </summary>
public class SigningCertificate
{
    /// <summary>
    /// The row's key.
    /// </summary>
    public int Key { get; set; }

    /// <summary>
    /// The year the certificate signs packages in.
    /// </summary>
    public int Year { get; set; }

    /// <summary>
    /// The lowercase hex SHA-256 fingerprint of the certificate.
    /// </summary>
    public string Fingerprint { get; set; } = null!;

    /// <summary>
    /// The DER-encoded certificate, without its private key.
    /// </summary>
    public byte[] Certificate { get; set; } = null!;

    /// <summary>
    /// The certificate with its private key, as a PKCS #12 file encrypted with ASP.NET Core data protection.
    /// </summary>
    public byte[] ProtectedPrivateKey { get; set; } = null!;

    /// <summary>
    /// When the certificate was generated, in UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the certificate expires, in UTC: the end of <see cref="Year"/>.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// When the certificate was revoked, in UTC, or null if it wasn't. A revoked certificate isn't advertised or
    /// used to sign, and packages it signed are re-signed when they're downloaded.
    /// </summary>
    public DateTime? RevokedAt { get; set; }
}
