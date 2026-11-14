namespace NuDepot.Gallery.Slices;

/// <summary>
/// A certificate the feed signs packages with.
/// </summary>
/// <param name="Fingerprint">The lowercase hex SHA-256 fingerprint that NuGet's trusted signers use.</param>
/// <param name="Subject">The certificate's subject.</param>
/// <param name="NotBefore">When the certificate becomes valid.</param>
/// <param name="NotAfter">When the certificate expires.</param>
/// <param name="IsSelfSigned">Whether the certificate signed itself, so clients have to allow an untrusted root.</param>
internal record SigningCertificateModel(string Fingerprint, string? Subject, DateTimeOffset NotBefore, DateTimeOffset NotAfter, bool IsSelfSigned);
