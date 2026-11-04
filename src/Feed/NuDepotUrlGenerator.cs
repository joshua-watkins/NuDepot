using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NuGet.Versioning;
using NuDepot.Feed.Configuration;
using System;

namespace NuDepot.Feed;

// TODO: This should validate the "Host" header against known valid values
public class NuDepotUrlGenerator : IUrlGenerator
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly LinkGenerator _linkGenerator;
    private readonly IOptions<FeedOptions> _options;

    public NuDepotUrlGenerator(
        IHttpContextAccessor httpContextAccessor,
        LinkGenerator linkGenerator,
        IOptions<FeedOptions> options)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _linkGenerator = linkGenerator ?? throw new ArgumentNullException(nameof(linkGenerator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string GetServiceIndexUrl()
    {
        return GetUrl(Routes.IndexRouteName);
    }

    public string GetPackageContentResourceUrl()
    {
        return AbsoluteUrl("v3/package");
    }

    public string GetPackageMetadataResourceUrl(bool semVer2 = false)
    {
        return AbsoluteUrl(semVer2 ? "v3/registration-semver2" : "v3/registration");
    }

    public string GetPackagePublishResourceUrl()
    {
        return GetUrl(Routes.UploadPackageRouteName);
    }

    public string GetSymbolPublishResourceUrl()
    {
        return GetUrl(Routes.UploadSymbolRouteName);
    }

    public string GetSearchResourceUrl()
    {
        return GetUrl(Routes.SearchRouteName);
    }

    public string GetAutocompleteResourceUrl()
    {
        return GetUrl(Routes.AutocompleteRouteName);
    }

    public string GetRegistrationIndexUrl(string id, bool semVer2 = false)
    {
        return GetUrl(
            semVer2 ? Routes.RegistrationSemVer2IndexRouteName : Routes.RegistrationIndexRouteName,
            new RouteValueDictionary
            {
                { "id", id.ToLowerInvariant() },
            });
    }

    public string GetRegistrationPageUrl(string id, NuGetVersion lower, NuGetVersion upper)
    {
        // NuDepot does not support paging the registration resource.
        throw new NotImplementedException();
    }

    public string GetRegistrationLeafUrl(string id, NuGetVersion version, bool semVer2 = false)
    {
        return GetUrl(
            semVer2 ? Routes.RegistrationSemVer2LeafRouteName : Routes.RegistrationLeafRouteName,
            new RouteValueDictionary
            {
                { "id", id.ToLowerInvariant() },
                { "version", version.ToNormalizedString().ToLowerInvariant() },
            });
    }

    public string GetPackageVersionsUrl(string id)
    {
        return GetUrl(
            Routes.PackageVersionsRouteName,
            new RouteValueDictionary
            {
                { "id", id.ToLowerInvariant() },
            });
    }

    public string GetPackageDownloadUrl(string id, NuGetVersion version)
    {
        id = id.ToLowerInvariant();
        var versionString = version.ToNormalizedString().ToLowerInvariant();

        return GetUrl(
            Routes.PackageDownloadRouteName,
            new RouteValueDictionary
            {
                { "id", id },
                { "version", versionString },
                { "idVersion", $"{id}.{versionString}" },
            });
    }

    public string GetPackageManifestDownloadUrl(string id, NuGetVersion version)
    {
        id = id.ToLowerInvariant();
        var versionString = version.ToNormalizedString().ToLowerInvariant();

        return GetUrl(
            Routes.PackageDownloadManifestRouteName,
            new RouteValueDictionary
            {
                { "id", id },
                { "version", versionString },
                { "id2", id },
            });
    }

    public string GetPackageIconDownloadUrl(string id, NuGetVersion version)
    {
        return GetUrl(Routes.PackageDownloadIconRouteName, PackageRouteValues(id, version));
    }

    public string GetPackageLicenseDownloadUrl(string id, NuGetVersion version, bool licenseIsMarkDown)
    {
        return GetUrl(
            licenseIsMarkDown ? Routes.PackageDownloadLicenseMarkDownRouteName : Routes.PackageDownloadLicenseTextRouteName,
            PackageRouteValues(id, version));
    }

    public string GetReadmeUriTemplate()
    {
        return AbsoluteUrl("v3/package/{lower_id}/{lower_version}/readme");
    }

    public string GetPackageDependentsResourceUrl()
    {
        return GetUrl(Routes.DependentsRouteName);
    }

    public string GetPackageReadmeDownloadUrl(string id, NuGetVersion version)
    {
        return GetUrl(Routes.PackageDownloadReadmeRouteName, PackageRouteValues(id, version));
    }

    public string GetCatalogIndexUrl()
    {
        return AbsoluteUrl("v3/catalog/index.json");
    }

    public string GetCatalogPageUrl(int page)
    {
        return AbsoluteUrl($"v3/catalog/page{page}.json");
    }

    public string GetCatalogLeafUrl(int key, string id, string version)
    {
        return AbsoluteUrl($"v3/catalog/data/{key}/{Uri.EscapeDataString(id.ToLowerInvariant())}.{Uri.EscapeDataString(version.ToLowerInvariant())}.json");
    }

    public string? GetPackageDetailsUriTemplate()
    {
        var template = _options.Value.PackageDetailsUriTemplate;
        if (string.IsNullOrEmpty(template)) return null;

        // An absolute template points somewhere else, like a separately hosted website.
        // A relative one is resolved against this server's address.
        return Uri.IsWellFormedUriString(template, UriKind.Absolute)
            ? template
            : AbsoluteUrl(template.TrimStart('/'));
    }

    public string GetRepositorySignaturesUrl(string resourceVersion)
    {
        return AbsoluteUrl($"v3/repository-signatures/{resourceVersion}/index.json");
    }

    public string GetRepositorySigningCertificateUrl(string sha256Fingerprint)
    {
        return AbsoluteUrl($"v3/repository-signatures/certificates/{sha256Fingerprint}.crt");
    }

    public string GetVulnerabilityIndexUrl()
    {
        return AbsoluteUrl("v3/vulnerabilities/index.json");
    }

    public string GetVulnerabilityFileUrl(string name)
    {
        return AbsoluteUrl($"v3/vulnerabilities/{name}.json");
    }

    private static RouteValueDictionary PackageRouteValues(string id, NuGetVersion version)
    {
        return new RouteValueDictionary
        {
            { "id", id.ToLowerInvariant() },
            { "version", version.ToNormalizedString().ToLowerInvariant() },
        };
    }

    private string GetUrl(string endpointName, RouteValueDictionary? values = null)
    {
        return _linkGenerator.GetUriByName(HttpContext, endpointName, values ?? new RouteValueDictionary())
            ?? throw new InvalidOperationException($"Could not generate a URL for the '{endpointName}' endpoint.");
    }

    private string AbsoluteUrl(string relativePath)
    {
        var request = HttpContext.Request;

        return string.Concat(
            request.Scheme,
            "://",
            request.Host.ToUriComponent(),
            request.PathBase.ToUriComponent(),
            "/",
            relativePath);
    }

    private HttpContext HttpContext => _httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("URLs can only be generated while handling a request.");
}
