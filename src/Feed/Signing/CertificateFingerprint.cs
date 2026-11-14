using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NuDepot.Feed.Signing;

/// <summary>
/// Certificate fingerprints, the way the repository signatures resource and NuGet's trusted signers write them.
/// </summary>
internal static class CertificateFingerprint
{
    /// <summary>
    /// Gets a certificate's fingerprint.
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The lowercase hex SHA-256 hash of the DER-encoded certificate.</returns>
    public static string Of(X509Certificate2 certificate) => Convert.ToHexStringLower(SHA256.HashData(certificate.RawData));
}
