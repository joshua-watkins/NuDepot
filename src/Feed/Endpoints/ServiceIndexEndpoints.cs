using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NuDepot.Core.Models;
using NuDepot.Core.Serialization;
using NuDepot.Feed.ServiceIndex;
using NuDepot.Feed.Signing;

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
        var response = await serviceIndex.GetAsync(cancellationToken);

        return Results.Json(response, NuGetJsonContext.Default.ServiceIndexResponse);
    }

    // GET v3/repository-signatures/{resourceVersion}/index.json
    public static async Task<IResult> RepositorySignaturesAsync(
        string resourceVersion,
        IRepositorySigningService signing,
        IPackageDatabase packages,
        IUrlGenerator url,
        CancellationToken cancellationToken)
    {
        var certificate = signing.Certificate;
        var fingerprint = signing.CertificateFingerprint;
        if (certificate == null || fingerprint == null) return Results.NotFound();

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

        var response = new RepositorySignaturesResponse
        {
            AllRepositorySigned = allRepositorySigned,
            SigningCertificates = new[]
            {
                new Core.Models.RepositorySigningCertificate
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
                }
            },
        };

        return Results.Json(response, NuGetJsonContext.Default.RepositorySignaturesResponse);
    }

    // GET v3/repository-signatures/certificates/{fingerprint}.crt
    public static IResult RepositorySigningCertificate(string fingerprint, IRepositorySigningService signing)
    {
        if (signing.Certificate == null ||
            !string.Equals(fingerprint, signing.CertificateFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        return Results.Bytes(signing.Certificate.Export(X509ContentType.Cert), "application/pkix-cert");
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
