using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Core.Extensions;
using NuGet.Core.Models;
using NuGet.Feed.Signing;
using NuGet.Feed.Vulnerabilities;

namespace NuGet.Feed.ServiceIndex;

public class NuGetFeedServiceIndex : IServiceIndexService
{
    private readonly IUrlGenerator _url;
    private readonly IVulnerabilityService _vulnerabilities;
    private readonly IRepositorySigningService _signing;

    public NuGetFeedServiceIndex(
        IUrlGenerator url,
        IVulnerabilityService vulnerabilities,
        IRepositorySigningService signing)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _vulnerabilities = vulnerabilities ?? throw new ArgumentNullException(nameof(vulnerabilities));
        _signing = signing ?? throw new ArgumentNullException(nameof(signing));
    }

    private IEnumerable<ServiceIndexItem> BuildResource(string name, string url, params string[] versions)
    {
        foreach (var version in versions)
        {
            var type = string.IsNullOrEmpty(version) ? name : $"{name}/{version}";

            yield return new ServiceIndexItem
            {
                ResourceUrl = url,
                Type = type,
            };
        }
    }

    public async Task<ServiceIndexResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var resources = new List<ServiceIndexItem>();

        resources.AddRange(BuildResource("PackagePublish", _url.GetPackagePublishResourceUrl(), "2.0.0"));
        resources.AddRange(BuildResource("SymbolPackagePublish", _url.GetSymbolPublishResourceUrl(), "4.9.0"));
        resources.AddRange(BuildResource("SearchQueryService", _url.GetSearchResourceUrl(), "", "3.0.0-beta", "3.0.0-rc", "3.5.0"));
        resources.AddRange(BuildResource("RegistrationsBaseUrl", _url.GetPackageMetadataResourceUrl(), "", "3.0.0-rc", "3.0.0-beta"));
        resources.AddRange(BuildResource("RegistrationsBaseUrl", _url.GetPackageMetadataResourceUrl(semVer2: true), "3.6.0"));
        resources.AddRange(BuildResource("PackageBaseAddress", _url.GetPackageContentResourceUrl(), "3.0.0"));
        resources.AddRange(BuildResource("SearchAutocompleteService", _url.GetAutocompleteResourceUrl(), "", "3.0.0-rc", "3.0.0-beta"));
        resources.AddRange(BuildResource(ServiceIndexModelExtensions.ReadmeUriTemplateType, _url.GetReadmeUriTemplate(), ""));
        resources.AddRange(BuildResource(ServiceIndexModelExtensions.PackageDependentsType, _url.GetPackageDependentsResourceUrl(), ""));
        resources.AddRange(BuildResource("Catalog", _url.GetCatalogIndexUrl(), "3.0.0"));

        // The protocol requires these resources to be served over HTTPS.
        var packageDetailsUriTemplate = _url.GetPackageDetailsUriTemplate();
        if (packageDetailsUriTemplate != null && IsHttps(packageDetailsUriTemplate))
        {
            resources.AddRange(BuildResource("PackageDetailsUriTemplate", packageDetailsUriTemplate, "5.1.0"));
        }

        if (_signing.Certificate != null && IsHttps(_url.GetServiceIndexUrl()))
        {
            resources.AddRange(BuildResource("RepositorySignatures", _url.GetRepositorySignaturesUrl("4.7.0"), "4.7.0", "4.9.0"));
            resources.AddRange(BuildResource("RepositorySignatures", _url.GetRepositorySignaturesUrl("5.0.0"), "5.0.0"));
        }

        // The protocol requires leaving this resource out if there is no vulnerability data.
        if (await _vulnerabilities.AnyAsync(cancellationToken))
        {
            resources.AddRange(BuildResource("VulnerabilityInfo", _url.GetVulnerabilityIndexUrl(), "6.7.0"));
        }

        return new ServiceIndexResponse
        {
            Version = "3.0.0",
            Resources = resources,
        };
    }

    private static bool IsHttps(string? url)
        => url != null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
