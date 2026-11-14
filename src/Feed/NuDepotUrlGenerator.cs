using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuGet.Versioning;

namespace NuDepot.Feed;

// TODO: This should validate the "Host" header against known valid values
/// <summary>
/// Generates the feed's URLs from its routes, for the address the request used.
/// </summary>
/// <param name="httpContextAccessor">The current request.</param>
/// <param name="linkGenerator">Generates URLs from routes.</param>
/// <param name="options">The feed's options.</param>
internal class NuDepotUrlGenerator(IHttpContextAccessor httpContextAccessor, LinkGenerator linkGenerator, IOptions<FeedOptions> options) : IUrlGenerator
{
    private IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;
    private LinkGenerator LinkGenerator { get; } = linkGenerator;
    private IOptions<FeedOptions> Options { get; } = options;

    /// <inheritdoc />
    public string GetServiceIndexUrl()
    {
        return GetUrl(Routes.IndexRouteName);
    }

    /// <inheritdoc />
    public string GetPackageContentResourceUrl()
    {
        return AbsoluteUrl("v3/package");
    }

    /// <inheritdoc />
    public string GetPackageMetadataResourceUrl(bool semVer2 = false)
    {
        return AbsoluteUrl(semVer2 ? "v3/registration-semver2" : "v3/registration");
    }

    /// <inheritdoc />
    public string GetPackagePublishResourceUrl()
    {
        return GetUrl(Routes.UploadPackageRouteName);
    }

    /// <inheritdoc />
    public string GetSymbolPublishResourceUrl()
    {
        return GetUrl(Routes.UploadSymbolRouteName);
    }

    /// <inheritdoc />
    public string GetSearchResourceUrl()
    {
        return GetUrl(Routes.SearchRouteName);
    }

    /// <inheritdoc />
    public string GetAutocompleteResourceUrl()
    {
        return GetUrl(Routes.AutocompleteRouteName);
    }

    /// <inheritdoc />
    public string GetRegistrationIndexUrl(string id, bool semVer2 = false)
    {
        return GetUrl(
            semVer2 ? Routes.RegistrationSemVer2IndexRouteName : Routes.RegistrationIndexRouteName,
            new RouteValueDictionary
            {
                { "id", id.ToLowerInvariant() },
            });
    }

    /// <inheritdoc />
    public string GetRegistrationPageUrl(string id, string lower, string upper, bool semVer2 = false)
    {
        return GetUrl(
            semVer2 ? Routes.RegistrationSemVer2PageRouteName : Routes.RegistrationPageRouteName,
            new RouteValueDictionary
            {
                { "id", id.ToLowerInvariant() },
                { "lower", lower },
                { "upper", upper },
            });
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public string GetPackageDownloadUrl(string id, NuGetVersion version)
    {
        id = id.ToLowerInvariant();
        string versionString = version
            .ToNormalizedString()
            .ToLowerInvariant();

        return GetUrl(
            Routes.PackageDownloadRouteName,
            new RouteValueDictionary
            {
                { "id", id },
                { "version", versionString },
                { "idVersion", $"{id}.{versionString}" },
            });
    }

    /// <inheritdoc />
    public string GetPackageIconDownloadUrl(string id, NuGetVersion version)
    {
        return GetUrl(Routes.PackageDownloadIconRouteName, PackageRouteValues(id, version));
    }

    /// <inheritdoc />
    public string GetPackageLicenseDownloadUrl(string id, NuGetVersion version, bool licenseIsMarkDown)
    {
        return GetUrl(
            licenseIsMarkDown ? Routes.PackageDownloadLicenseMarkDownRouteName : Routes.PackageDownloadLicenseTextRouteName,
            PackageRouteValues(id, version));
    }

    /// <inheritdoc />
    public string GetReadmeUriTemplate()
    {
        return AbsoluteUrl("v3/package/{lower_id}/{lower_version}/readme");
    }

    /// <inheritdoc />
    public string GetPackageDependentsResourceUrl()
    {
        return GetUrl(Routes.DependentsRouteName);
    }

    /// <inheritdoc />
    public string GetPackageReadmeDownloadUrl(string id, NuGetVersion version)
    {
        return GetUrl(Routes.PackageDownloadReadmeRouteName, PackageRouteValues(id, version));
    }

    /// <inheritdoc />
    public string GetCatalogIndexUrl()
    {
        return AbsoluteUrl("v3/catalog/index.json");
    }

    /// <inheritdoc />
    public string GetCatalogPageUrl(int page)
    {
        return AbsoluteUrl($"v3/catalog/page{page}.json");
    }

    /// <inheritdoc />
    public string GetCatalogLeafUrl(int key, string id, string version)
    {
        return AbsoluteUrl($"v3/catalog/data/{key}/{Uri.EscapeDataString(id.ToLowerInvariant())}.{Uri.EscapeDataString(version.ToLowerInvariant())}.json");
    }

    /// <inheritdoc />
    public string? GetPackageDetailsUriTemplate()
    {
        string? template = Options.Value.PackageDetailsUriTemplate;
        if (string.IsNullOrEmpty(template))
            return null;

        // An absolute template points somewhere else, like a separately hosted website.
        // A relative one is resolved against this server's address.
        return Uri.IsWellFormedUriString(template, UriKind.Absolute)
            ? template
            : AbsoluteUrl(template.TrimStart('/'));
    }

    /// <inheritdoc />
    public string GetRepositorySignaturesUrl(string resourceVersion)
    {
        return AbsoluteUrl($"v3/repository-signatures/{resourceVersion}/index.json");
    }

    /// <inheritdoc />
    public string GetRepositorySigningCertificateUrl(string sha256Fingerprint)
    {
        return AbsoluteUrl($"v3/repository-signatures/certificates/{sha256Fingerprint}.crt");
    }

    /// <inheritdoc />
    public string GetVulnerabilityIndexUrl()
    {
        return AbsoluteUrl("v3/vulnerabilities/index.json");
    }

    /// <inheritdoc />
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
        return LinkGenerator.GetUriByName(HttpContext, endpointName, values ?? [])
            ?? throw new InvalidOperationException($"Could not generate a URL for the '{endpointName}' endpoint.");
    }

    private string AbsoluteUrl(string relativePath)
    {
        HttpRequest request = HttpContext.Request;

        return string.Concat(
            request.Scheme,
            "://",
            request.Host.ToUriComponent(),
            request.PathBase.ToUriComponent(),
            "/",
            relativePath);
    }

    private HttpContext HttpContext => HttpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("URLs can only be generated while handling a request.");
}
