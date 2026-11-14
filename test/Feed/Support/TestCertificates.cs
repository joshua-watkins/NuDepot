using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NuDepot.Feed.Tests.Support;

/// <summary>
/// Creates certificates for tests.
/// </summary>
internal static class TestCertificates
{
    /// <summary>
    /// Create a self-signed code signing certificate and save it, with its private key, as a PFX file.
    /// </summary>
    public static X509Certificate2 CreateCodeSigningPfx(string path, string password)
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new(
            "CN=NuDepot Test Repository",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.3")],
            critical: false));

        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));

        return X509CertificateLoader.LoadCertificate(certificate.RawData);
    }
}
