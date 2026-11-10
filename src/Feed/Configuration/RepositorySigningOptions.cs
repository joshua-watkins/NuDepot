namespace NuDepot.Feed.Configuration;

/// <summary>
/// How the server repository signs pushed packages. The options are bound from the <see cref="Section"/>
/// configuration section only once the application adds signing, with <c>AddSigning()</c> or
/// <c>EnableSigning()</c>.
/// See: https://learn.microsoft.com/en-us/nuget/api/repository-signatures-resource
/// </summary>
public class RepositorySigningOptions
{
    /// <summary>
    /// The configuration section the options are bound from.
    /// </summary>
    public const string Section = "Feed:RepositorySigning";

    /// <summary>
    /// Whether pushed packages are repository signed. True by default with <c>AddSigning()</c>, and false by
    /// default with <c>EnableSigning()</c>. Either way, the configuration can change it. Without either, it's
    /// false, and the configuration is ignored. When it's on, the feed only accepts HTTPS requests, since
    /// signatures name the feed's HTTPS address. Without <see cref="CertificatePath"/> or
    /// <see cref="CertificateThumbprint"/>, the feed generates a self-signed certificate each year. NuGet clients
    /// don't trust self-signed certificates, so they warn about every package, and packages signed by an older
    /// certificate are re-signed when they're downloaded, which changes their hash.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// The path to a PFX file holding the signing certificate and its private key. A relative path is relative
    /// to the working directory.
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

    /// <summary>
    /// Whether a certificate is configured, with <see cref="CertificatePath"/> or <see cref="CertificateThumbprint"/>.
    /// </summary>
    public bool HasCertificate => !string.IsNullOrEmpty(CertificatePath) || !string.IsNullOrEmpty(CertificateThumbprint);

    /// <summary>
    /// Whether the feed signs with certificates it generates, because signing is on without a configured certificate.
    /// </summary>
    public bool GeneratesCertificates => IsEnabled && !HasCertificate;
}
