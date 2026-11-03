using NuGet.Versioning;

namespace NuGet.Feed;

/// <summary>
/// Used to create URLs to resources in the NuGet protocol.
/// </summary>
public interface IUrlGenerator
{
    /// <summary>
    /// Get the URL for the package source (also known as the "service index").
    /// See: https://docs.microsoft.com/en-us/nuget/api/service-index
    /// </summary>
    string GetServiceIndexUrl();

    /// <summary>
    /// Get the URL for the root of the package content resource.
    /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource
    /// </summary>
    string GetPackageContentResourceUrl();

    /// <summary>
    /// Get the URL for the root of the package metadata resource.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
    /// </summary>
    string GetPackageMetadataResourceUrl(bool semVer2 = false);

    /// <summary>
    /// Get the URL to publish packages.
    /// See: https://docs.microsoft.com/en-us/nuget/api/package-publish-resource
    /// </summary>
    string GetPackagePublishResourceUrl();

    /// <summary>
    /// Get the URL to publish symbol packages.
    /// See: https://docs.microsoft.com/en-us/nuget/api/symbol-package-publish-resource
    /// </summary>
    string GetSymbolPublishResourceUrl();

    /// <summary>
    /// Get the URL to search for packages.
    /// See: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource
    /// </summary>
    string GetSearchResourceUrl();

    /// <summary>
    /// Get the URL to autocomplete package IDs.
    /// See: https://docs.microsoft.com/en-us/nuget/api/search-query-service-resource
    /// </summary>
    string GetAutocompleteResourceUrl();

    /// <summary>
    /// Get the URL for the entry point of a package's metadata.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-index
    /// </summary>
    /// <param name="id">The package's ID.</param>
    string GetRegistrationIndexUrl(string id, bool semVer2 = false);

    /// <summary>
    /// Get the URL for the metadata of several versions of a single package.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-page
    /// </summary>
    /// <param name="id">The package's ID</param>
    /// <param name="lower">The lowest SemVer 2.0.0 version in the page (inclusive)</param>
    /// <param name="upper">The highest SemVer 2.0.0 version in the page (inclusive)</param>
    string GetRegistrationPageUrl(string id, NuGetVersion lower, NuGetVersion upper);

    /// <summary>
    /// Get the URL for the metadata of a specific package ID and version.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-leaf
    /// </summary>
    /// <param name="id">The package's ID</param>
    /// <param name="version">The package's version</param>
    string GetRegistrationLeafUrl(string id, NuGetVersion version, bool semVer2 = false);

    /// <summary>
    /// Get the URL that lists a package's versions.
    /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource#enumerate-package-versions
    /// </summary>
    /// <param name="id">The package's ID</param>
    string GetPackageVersionsUrl(string id);

    /// <summary>
    /// Get the URL to download a package (.nupkg).
    /// See: https://docs.microsoft.com/en-us/nuget/api/package-base-address-resource#download-package-content-nupkg
    /// </summary>
    /// <param name="id">The package's ID</param>
    /// <param name="version">The package's version</param>
    string GetPackageDownloadUrl(string id, NuGetVersion version);

    /// <summary>
    /// Get the URL to download a package's manifest (.nuspec).
    /// </summary>
    /// <param name="id">The package's ID</param>
    /// <param name="version">The package's version</param>
    string GetPackageManifestDownloadUrl(string id, NuGetVersion version);

    /// <summary>
    /// Get the URL to download a package icon.
    /// </summary>
    /// <param name="id">The package's ID</param>
    /// <param name="version">The package's version</param>
    string GetPackageIconDownloadUrl(string id, NuGetVersion version);

    /// <summary>
    /// Get the URL to download the package's license.
    /// </summary>
    /// <param name="id">The package's ID</param>
    /// <param name="version">The package's version</param>
    /// <param name="licenseIsMarkDown">Indicates if the license format is MarkDown (true) or Text (false).</param>
    /// <returns></returns>
    string GetPackageLicenseDownloadUrl(string id, NuGetVersion version, bool licenseIsMarkDown);

    /// <summary>
    /// Get the URL template used to download packages' readmes.
    /// See: https://learn.microsoft.com/en-us/nuget/api/readme-template-resource
    /// </summary>
    string GetReadmeUriTemplate();

    /// <summary>
    /// Get the URL to find the packages that depend on a package.
    /// This is an unofficial API that isn't part of the NuGet protocol.
    /// </summary>
    string GetPackageDependentsResourceUrl();

    /// <summary>
    /// Get the URL to download a package's readme.
    /// </summary>
    string GetPackageReadmeDownloadUrl(string id, NuGetVersion version);

    /// <summary>
    /// Get the URL to the catalog index.
    /// See: https://learn.microsoft.com/en-us/nuget/api/catalog-resource
    /// </summary>
    string GetCatalogIndexUrl();

    /// <summary>
    /// Get the URL to a catalog page.
    /// </summary>
    /// <param name="page">The page's zero-based index.</param>
    string GetCatalogPageUrl(int page);

    /// <summary>
    /// Get the URL to a catalog leaf.
    /// </summary>
    /// <param name="key">The catalog entry's key.</param>
    /// <param name="id">The package's ID.</param>
    /// <param name="version">The package's full normalized version.</param>
    string GetCatalogLeafUrl(int key, string id, string version);

    /// <summary>
    /// Get the URL template users can open to see a package's details, or null if none is configured.
    /// See: https://learn.microsoft.com/en-us/nuget/api/package-details-template-resource
    /// </summary>
    string? GetPackageDetailsUriTemplate();

    /// <summary>
    /// Get the URL to the repository signatures resource.
    /// See: https://learn.microsoft.com/en-us/nuget/api/repository-signatures-resource
    /// </summary>
    /// <param name="resourceVersion">"4.7.0" or "5.0.0".</param>
    string GetRepositorySignaturesUrl(string resourceVersion);

    /// <summary>
    /// Get the URL to download a repository signing certificate.
    /// </summary>
    /// <param name="sha256Fingerprint">The certificate's lowercase hex SHA-256 fingerprint.</param>
    string GetRepositorySigningCertificateUrl(string sha256Fingerprint);

    /// <summary>
    /// Get the URL to the vulnerability index.
    /// See: https://learn.microsoft.com/en-us/nuget/api/vulnerability-info
    /// </summary>
    string GetVulnerabilityIndexUrl();

    /// <summary>
    /// Get the URL to a vulnerability file.
    /// </summary>
    string GetVulnerabilityFileUrl(string name);
}
