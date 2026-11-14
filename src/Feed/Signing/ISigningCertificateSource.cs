using System.Security.Cryptography.X509Certificates;

namespace NuDepot.Feed.Signing;

/// <summary>
/// Where the repository signing certificates come from: the configured certificate, or the generated ones.
/// The source owns the certificates it returns, so callers don't dispose them.
/// </summary>
public interface ISigningCertificateSource
{
    /// <summary>
    /// The hash algorithm of signatures made with the source's certificates.
    /// </summary>
    NuGet.Common.HashAlgorithmName HashAlgorithm { get; }

    /// <summary>
    /// Whether the certificates are self-signed, so NuGet can't build a chain to a trusted root while signing.
    /// </summary>
    bool AllowUntrustedRoot { get; }

    /// <summary>
    /// Gets the certificate that signs packages now, with its private key.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The certificate.</returns>
    Task<X509Certificate2> GetSigningCertificateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the certificates clients should trust, without their private keys: those that haven't expired.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The certificates, oldest first.</returns>
    Task<IReadOnlyList<X509Certificate2>> GetAdvertisedCertificatesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Finds one of the source's certificates, even if it expired.
    /// </summary>
    /// <param name="fingerprint">The certificate's lowercase hex SHA-256 fingerprint.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The certificate, without its private key, or null if it isn't one of the source's.</returns>
    Task<X509Certificate2?> FindCertificateAsync(string fingerprint, CancellationToken cancellationToken);
}
