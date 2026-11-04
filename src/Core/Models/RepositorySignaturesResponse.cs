using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

/// <summary>
/// The package source's repository signing capabilities.
///
/// See https://learn.microsoft.com/en-us/nuget/api/repository-signatures-resource
/// </summary>
public class RepositorySignaturesResponse
{
    /// <summary>
    /// Whether all packages on the source are repository signed by one of the
    /// <see cref="SigningCertificates"/>. Must be false on the 4.7.0 and 4.9.0 resources.
    /// </summary>
    [JsonPropertyName("allRepositorySigned")]
    public bool AllRepositorySigned { get; set; }

    /// <summary>
    /// The certificates the source uses to sign packages.
    /// </summary>
    [JsonPropertyName("signingCertificates")]
    public IReadOnlyList<RepositorySigningCertificate> SigningCertificates { get; set; } = null!;
}

/// <summary>
/// A certificate the package source uses to repository sign packages.
/// </summary>
public class RepositorySigningCertificate
{
    /// <summary>
    /// The OID of the SHA-256 hash algorithm, the key of <see cref="Fingerprints"/>.
    /// </summary>
    public const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    /// <summary>
    /// The absolute URL to the DER-encoded public certificate.
    /// </summary>
    [JsonPropertyName("contentUrl")]
    public string ContentUrl { get; set; } = null!;

    /// <summary>
    /// The certificate's fingerprints, keyed by hash algorithm OID. Values are lowercase hex.
    /// </summary>
    [JsonPropertyName("fingerprints")]
    public IReadOnlyDictionary<string, string> Fingerprints { get; set; } = null!;

    /// <summary>
    /// The certificate's subject distinguished name.
    /// </summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    /// <summary>
    /// The certificate's issuer distinguished name.
    /// </summary>
    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    /// <summary>
    /// When the certificate becomes valid.
    /// </summary>
    [JsonPropertyName("notBefore")]
    public DateTimeOffset NotBefore { get; set; }

    /// <summary>
    /// When the certificate expires.
    /// </summary>
    [JsonPropertyName("notAfter")]
    public DateTimeOffset NotAfter { get; set; }
}
