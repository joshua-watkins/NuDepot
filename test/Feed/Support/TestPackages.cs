using NuGet.Packaging;
using NuGet.Packaging.Licenses;
using NuGet.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace NuDepot.Feed.Tests.Support;

/// <summary>
/// Builds packages for tests.
/// </summary>
internal static class TestPackages
{
    public static MemoryStream Create(string id, string version, string description = "Test description", string? title = null, string[]? tags = null, string author = "Test author", string? packageType = null, string? licenseExpression = null)
    {
        PackageBuilder builder = new()
        {
            Id = id,
            Version = NuGetVersion.Parse(version),
            Description = description,
            Title = title,
        };

        builder.Authors.Add(author);
        builder.Tags.AddRange(tags ?? []);

        if (packageType != null)
            builder.PackageTypes.Add(new NuGet.Packaging.Core.PackageType(packageType, new Version(0, 0)));

        if (licenseExpression != null)
        {
            builder.LicenseMetadata = new LicenseMetadata(
                LicenseType.Expression,
                licenseExpression,
                NuGetLicenseExpression.Parse(licenseExpression),
                warningsAndErrors: null,
                LicenseMetadata.EmptyVersion);
        }

        // A package needs content or dependencies.
        builder.Files.Add(new PhysicalPackageFile(new MemoryStream(Encoding.UTF8.GetBytes("test")))
        {
            TargetPath = "lib/net8.0/_._",
        });

        MemoryStream stream = new();
        builder.Save(stream);
        stream.Position = 0;

        return stream;
    }
}

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
