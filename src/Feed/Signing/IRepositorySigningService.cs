using System.Security.Cryptography.X509Certificates;

namespace NuDepot.Feed.Signing;

/// <summary>
/// Adds this server's repository signature to pushed packages, and re-signs packages signed with an older certificate.
/// See: https://learn.microsoft.com/en-us/nuget/api/repository-signatures-resource
/// </summary>
public interface IRepositorySigningService
{
    /// <summary>
    /// Whether the feed repository signs packages.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets the certificates clients should trust: those that haven't expired. Empty if signing is off.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The certificates, without their private keys. The service owns them, so callers don't dispose them.</returns>
    Task<IReadOnlyList<X509Certificate2>> GetCertificatesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Finds one of the feed's signing certificates, even if it expired.
    /// </summary>
    /// <param name="fingerprint">The certificate's lowercase hex SHA-256 fingerprint.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The certificate, which the service owns, or null if it isn't one of the feed's.</returns>
    Task<X509Certificate2?> FindCertificateAsync(string fingerprint, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the fingerprint of the certificate that signs packages now.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The lowercase hex SHA-256 fingerprint, or null if signing is off.</returns>
    Task<string?> GetSigningFingerprintAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Repository sign a package, if signing is on and the package doesn't already have a repository signature.
    /// </summary>
    /// <param name="package">The package. Its position is reset afterwards.</param>
    /// <param name="serviceIndexUrl">This server's service index URL, which the signature records. Must be HTTPS.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The signed package, if it was signed.</returns>
    Task<RepositorySigningResult> SignAsync(Stream package, string serviceIndexUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Replace a package's repository signature with one from the current certificate. Its author signature, if it
    /// has one, is kept. The caller checks that the repository signature is this server's.
    /// </summary>
    /// <param name="package">The package. Its position is reset afterwards.</param>
    /// <param name="serviceIndexUrl">This server's service index URL, which the signature records. Must be HTTPS.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The re-signed package, or <see cref="RepositorySigningResult.Unsigned"/> if it couldn't be re-signed.</returns>
    Task<RepositorySigningResult> ResignAsync(Stream package, string serviceIndexUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a package's repository signature, whichever repository made it.
    /// </summary>
    /// <param name="package">The package. Its position is reset afterwards.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The signature, or null if the package doesn't have a repository signature.</returns>
    Task<RepositorySignatureDetails?> GetRepositorySignatureAsync(Stream package, CancellationToken cancellationToken);
}
