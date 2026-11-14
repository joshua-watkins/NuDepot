namespace NuDepot.Feed.Signing;

/// <summary>
/// The result of repository signing a package.
/// </summary>
/// <param name="SignedPackage">The signed package, or null if the package wasn't changed. The caller disposes it.</param>
/// <param name="IsRepositorySigned">Whether the package now carries this server's repository signature.</param>
/// <param name="Fingerprint">The fingerprint of the certificate that made this server's repository signature, if it has one.</param>
public sealed record RepositorySigningResult(Stream? SignedPackage, bool IsRepositorySigned, string? Fingerprint = null)
{
    /// <summary>
    /// The result for a package that wasn't signed.
    /// </summary>
    public static readonly RepositorySigningResult Unsigned = new(null, false);
}
