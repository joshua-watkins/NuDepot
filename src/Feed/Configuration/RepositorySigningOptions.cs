namespace NuGet.Feed.Configuration;

/// <summary>
/// How the server repository signs pushed packages.
/// See: https://learn.microsoft.com/en-us/nuget/api/repository-signatures-resource
/// </summary>
public class RepositorySigningOptions
{
    /// <summary>
    /// The path to a PFX file holding the signing certificate and its private key.
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// The PFX file's password, if it has one.
    /// </summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// The thumbprint of a signing certificate in a certificate store, used instead of <see cref="CertificatePath"/>.
    /// </summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>
    /// "CurrentUser" or "LocalMachine". Used with <see cref="CertificateThumbprint"/>.
    /// </summary>
    public string CertificateStoreLocation { get; set; } = "CurrentUser";

    /// <summary>
    /// The certificate store's name, usually "My". Used with <see cref="CertificateThumbprint"/>.
    /// </summary>
    public string CertificateStoreName { get; set; } = "My";

    /// <summary>
    /// An RFC 3161 timestamp server. Recommended: without a timestamp, signatures stop
    /// being valid when the certificate expires.
    /// </summary>
    public string? TimestampServerUrl { get; set; }

    public bool IsEnabled => !string.IsNullOrEmpty(CertificatePath) || !string.IsNullOrEmpty(CertificateThumbprint);
}
