using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Feed.Signing;
using NuDepot.Feed.Vulnerabilities;

namespace NuDepot.Feed.ServiceIndex;

/// <summary>
/// Builds the feed's service index, which lists its resources.
/// </summary>
/// <param name="url">Generates the feed's URLs.</param>
/// <param name="vulnerabilities">Whether the feed has known vulnerabilities to list.</param>
/// <param name="signing">Whether the feed signs packages.</param>
public class NuDepotServiceIndex(IUrlGenerator url, IVulnerabilityService vulnerabilities, IRepositorySigningService signing) : IServiceIndexService
{
    private IUrlGenerator Url { get; } = url;
    private IVulnerabilityService Vulnerabilities { get; } = vulnerabilities;
    private IRepositorySigningService Signing { get; } = signing;

    private IEnumerable<ServiceIndexItem> BuildResource(string name, string url, params string[] versions)
    {
        foreach (string version in versions)
        {
            string type = string.IsNullOrEmpty(version) ? name : $"{name}/{version}";

            yield return new ServiceIndexItem
            {
                ResourceUrl = url,
                Type = type,
            };
        }
    }

    /// <inheritdoc />
    public async Task<ServiceIndexResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        List<ServiceIndexItem> resources =
        [
            .. BuildResource("PackagePublish", Url.GetPackagePublishResourceUrl(), "2.0.0"),
            .. BuildResource("SymbolPackagePublish", Url.GetSymbolPublishResourceUrl(), "4.9.0"),
            .. BuildResource("SearchQueryService", Url.GetSearchResourceUrl(), "", "3.0.0-beta", "3.0.0-rc", "3.5.0"),
            .. BuildResource("RegistrationsBaseUrl", Url.GetPackageMetadataResourceUrl(), "", "3.0.0-rc", "3.0.0-beta"),
            .. BuildResource("RegistrationsBaseUrl", Url.GetPackageMetadataResourceUrl(semVer2: true), "3.6.0"),
            .. BuildResource("PackageBaseAddress", Url.GetPackageContentResourceUrl(), "3.0.0"),
            .. BuildResource("SearchAutocompleteService", Url.GetAutocompleteResourceUrl(), "", "3.0.0-rc", "3.0.0-beta"),
            .. BuildResource(ServiceIndexModelExtensions.ReadmeUriTemplateType, Url.GetReadmeUriTemplate(), ""),
            .. BuildResource(ServiceIndexModelExtensions.PackageDependentsType, Url.GetPackageDependentsResourceUrl(), ""),
            .. BuildResource("Catalog", Url.GetCatalogIndexUrl(), "3.0.0"),
        ];

        // The protocol requires these resources to be served over HTTPS.
        string? packageDetailsUriTemplate = Url.GetPackageDetailsUriTemplate();
        if (packageDetailsUriTemplate != null && IsHttps(packageDetailsUriTemplate))
            resources.AddRange(BuildResource("PackageDetailsUriTemplate", packageDetailsUriTemplate, "5.1.0"));

        if (Signing.IsEnabled && IsHttps(Url.GetServiceIndexUrl()))
        {
            resources.AddRange(BuildResource("RepositorySignatures", Url.GetRepositorySignaturesUrl("4.7.0"), "4.7.0", "4.9.0"));
            resources.AddRange(BuildResource("RepositorySignatures", Url.GetRepositorySignaturesUrl("5.0.0"), "5.0.0"));
        }

        // The protocol requires leaving this resource out if there is no vulnerability data.
        if (await Vulnerabilities.AnyAsync(cancellationToken))
            resources.AddRange(BuildResource("VulnerabilityInfo", Url.GetVulnerabilityIndexUrl(), "6.7.0"));

        return new ServiceIndexResponse
        {
            Version = "3.0.0",
            Resources = resources,
        };
    }

    private static bool IsHttps(string? url)
        => url != null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
