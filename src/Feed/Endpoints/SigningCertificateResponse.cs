namespace NuDepot.Feed.Endpoints;

/// <summary>
/// A certificate the feed signs, or signed, packages with.
/// </summary>
internal class SigningCertificateResponse
{
    /// <summary>
    /// The lowercase hex SHA-256 fingerprint.
    /// </summary>
    public string Fingerprint { get; set; } = null!;

    /// <summary>
    /// The certificate's subject.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// When the certificate became valid.
    /// </summary>
    public DateTimeOffset NotBefore { get; set; }

    /// <summary>
    /// When the certificate expires.
    /// </summary>
    public DateTimeOffset NotAfter { get; set; }

    /// <summary>
    /// When the certificate was revoked, in UTC, if it was replaced early.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Whether the feed signs packages with it now.
    /// </summary>
    public bool IsCurrent { get; set; }
}
