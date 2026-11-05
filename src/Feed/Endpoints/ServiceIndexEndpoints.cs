using Microsoft.AspNetCore.Http;
using NuDepot.Core.Models;
using NuDepot.Core.Serialization;
using NuDepot.Feed.ServiceIndex;
using NuDepot.Feed.Signing;
using System.Security.Cryptography.X509Certificates;

namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The NuGet Service Index, which helps NuGet clients discover this server's resources,
/// and the Repository Signatures resource, which tells clients which certificates sign its packages.
/// See: https://docs.microsoft.com/en-us/nuget/api/service-index
/// See: https://learn.microsoft.com/en-us/nuget/api/repository-signatures-resource
/// </summary>
internal static class ServiceIndexEndpoints
{
    // GET v3/index.json
    public static async Task<IResult> ServiceIndexAsync(IServiceIndexService serviceIndex, CancellationToken cancellationToken)
    {
        ServiceIndexResponse response = await serviceIndex.GetAsync(cancellationToken);

        return Results.Json(response, NuGetJsonContext.Default.ServiceIndexResponse);
    }

    // GET v3/repository-signatures/{resourceVersion}/index.json
    public static async Task<IResult> RepositorySignaturesAsync(string resourceVersion, IRepositorySigningService signing, IPackageDatabase packages, IUrlGenerator url, CancellationToken cancellationToken)
    {
        if (!signing.IsEnabled)
            return Results.NotFound();

        bool allRepositorySigned;
        switch (resourceVersion)
        {
            // NuGet 4.7 to 4.9 clients can't install packages from sources that claim
            // all packages are signed, so the older resource must always say false.
            case "4.7.0":
                allRepositorySigned = false;
                break;

            case "5.0.0":
                allRepositorySigned = await packages.AllRepositorySignedAsync(cancellationToken);
                break;

            default:
                return Results.NotFound();
        }

        // Every certificate that hasn't expired or been revoked.
        IReadOnlyList<X509Certificate2> certificates = await signing.GetCertificatesAsync(cancellationToken);

        // When a feed says all its packages are repository signed, NuGet requires a trusted certificate from this
        // list, so a self-signed certificate fails every restore, and a client with an older copy of the list
        // rejects packages signed with a newer certificate. Otherwise untrusted certificates are only warnings.
        if (certificates.Any(IsSelfSigned))
            allRepositorySigned = false;
        RepositorySignaturesResponse response = new()
        {
            AllRepositorySigned = allRepositorySigned,
            SigningCertificates = [.. certificates.Select(certificate =>
            {
                string fingerprint = CertificateFingerprint.Of(certificate);
                return new Core.Models.RepositorySigningCertificate
                {
                    ContentUrl = url.GetRepositorySigningCertificateUrl(fingerprint),
                    Fingerprints = new Dictionary<string, string>
                    {
                        { Core.Models.RepositorySigningCertificate.Sha256Oid, fingerprint },
                    },
                    Subject = certificate.Subject,
                    Issuer = certificate.Issuer,
                    NotBefore = certificate.NotBefore.ToUniversalTime(),
                    NotAfter = certificate.NotAfter.ToUniversalTime(),
                };
            })],
        };

        return Results.Json(response, NuGetJsonContext.Default.RepositorySignaturesResponse);
    }

    private static bool IsSelfSigned(X509Certificate2 certificate)
        => certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData);

    // GET v3/repository-signatures/certificates/{fingerprint}.crt
    public static async Task<IResult> RepositorySigningCertificateAsync(string fingerprint, IRepositorySigningService signing, CancellationToken cancellationToken)
    {
        X509Certificate2? certificate = await signing.FindCertificateAsync(fingerprint, cancellationToken);
        if (certificate == null)
            return Results.NotFound();

        return Results.Bytes(certificate.Export(X509ContentType.Cert), "application/pkix-cert");
    }

    // GET api/v2/{**path}
    // NuDepot only serves the NuGet V3 protocol. This answers NuGet V2 (OData) feed
    // requests with a message that points clients to the V3 service index.
    public static IResult V2FeedNotSupported(IUrlGenerator url)
    {
        return EndpointHelpers.Text(
            StatusCodes.Status404NotFound,
            "The NuGet V2 (OData) feed is not supported. " +
            $"Use the NuGet V3 service index at {url.GetServiceIndexUrl()} instead.");
    }
}
