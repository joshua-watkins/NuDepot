using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace NuDepot.Feed.Signing;

/// <summary>
/// Adds this server's repository signature to pushed packages.
/// See: https://learn.microsoft.com/en-us/nuget/api/repository-signatures-resource
/// </summary>
public interface IRepositorySigningService
{
    /// <summary>
    /// The signing certificate, or null if repository signing is off.
    /// </summary>
    X509Certificate2? Certificate { get; }

    /// <summary>
    /// The certificate's lowercase hex SHA-256 fingerprint, or null if repository signing is off.
    /// </summary>
    string? CertificateFingerprint { get; }

    /// <summary>
    /// Repository sign a package, if signing is on and the package doesn't already have a repository signature.
    /// </summary>
    /// <param name="package">The package. Its position is reset afterwards.</param>
    /// <param name="serviceIndexUrl">This server's service index URL, which the signature records. Must be HTTPS.</param>
    Task<RepositorySigningResult> SignAsync(Stream package, string serviceIndexUrl, CancellationToken cancellationToken);
}

/// <param name="SignedPackage">The signed package, or null if the package wasn't changed. The caller disposes it.</param>
/// <param name="IsRepositorySigned">Whether the package now carries this server's repository signature.</param>
public sealed record RepositorySigningResult(Stream? SignedPackage, bool IsRepositorySigned)
{
    public static readonly RepositorySigningResult Unsigned = new(null, false);
}
