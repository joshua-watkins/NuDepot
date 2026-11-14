namespace NuDepot.Feed.Signing;

/// <summary>
/// The size of the RSA keys of generated signing certificates. Tests use smaller keys, which generate faster.
/// </summary>
/// <param name="Bits">The key size, in bits.</param>
internal sealed record GeneratedCertificateKeySize(int Bits)
{
    /// <summary>
    /// The size the feed uses.
    /// </summary>
    public static GeneratedCertificateKeySize Default { get; } = new(8192);
}
