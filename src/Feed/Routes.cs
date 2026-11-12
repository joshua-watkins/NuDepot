namespace NuDepot.Feed;

/// <summary>
/// The names of the feed's routes, which its URLs are generated from.
/// </summary>
public class Routes
{
    /// <summary>
    /// The service index.
    /// </summary>
    public const string IndexRouteName = "index";

    /// <summary>
    /// Pushing a package.
    /// </summary>
    public const string UploadPackageRouteName = "upload-package";

    /// <summary>
    /// Pushing a symbol package.
    /// </summary>
    public const string UploadSymbolRouteName = "upload-symbol";

    /// <summary>
    /// Deleting a package version.
    /// </summary>
    public const string DeleteRouteName = "delete";

    /// <summary>
    /// Relisting a package version.
    /// </summary>
    public const string RelistRouteName = "relist";

    /// <summary>
    /// The NuGet V2 feed, which isn't supported.
    /// </summary>
    public const string V2FeedRouteName = "v2-feed";

    /// <summary>
    /// Searching packages.
    /// </summary>
    public const string SearchRouteName = "search";

    /// <summary>
    /// Autocompleting package IDs.
    /// </summary>
    public const string AutocompleteRouteName = "autocomplete";

    /// <summary>
    /// A package's dependents.
    /// </summary>
    public const string DependentsRouteName = "dependents";

    /// <summary>
    /// A package's registration index.
    /// </summary>
    public const string RegistrationIndexRouteName = "registration-index";

    /// <summary>
    /// A page of a package's registration, for packages with too many versions to list in the index.
    /// </summary>
    public const string RegistrationPageRouteName = "registration-page";

    /// <summary>
    /// A package version's registration leaf.
    /// </summary>
    public const string RegistrationLeafRouteName = "registration-leaf";

    /// <summary>
    /// A package's registration index, in the hive that includes SemVer 2.0.0 packages.
    /// </summary>
    public const string RegistrationSemVer2IndexRouteName = "registration-semver2-index";

    /// <summary>
    /// A page of a package's registration, in the hive that includes SemVer 2.0.0 packages.
    /// </summary>
    public const string RegistrationSemVer2PageRouteName = "registration-semver2-page";

    /// <summary>
    /// A package version's registration leaf, in the hive that includes SemVer 2.0.0 packages.
    /// </summary>
    public const string RegistrationSemVer2LeafRouteName = "registration-semver2-leaf";

    /// <summary>
    /// The catalog's index.
    /// </summary>
    public const string CatalogIndexRouteName = "catalog-index";

    /// <summary>
    /// A catalog page.
    /// </summary>
    public const string CatalogPageRouteName = "catalog-page";

    /// <summary>
    /// A catalog leaf.
    /// </summary>
    public const string CatalogLeafRouteName = "catalog-leaf";

    /// <summary>
    /// Deprecating a package version.
    /// </summary>
    public const string DeprecationRouteName = "deprecation";

    /// <summary>
    /// Managing known vulnerabilities.
    /// </summary>
    public const string VulnerabilitiesRouteName = "vulnerabilities";

    /// <summary>
    /// The vulnerability resource's index.
    /// </summary>
    public const string VulnerabilityIndexRouteName = "vulnerability-index";

    /// <summary>
    /// A vulnerability file.
    /// </summary>
    public const string VulnerabilityFileRouteName = "vulnerability-file";

    /// <summary>
    /// The repository signatures resource.
    /// </summary>
    public const string RepositorySignaturesRouteName = "repository-signatures";

    /// <summary>
    /// Downloading the repository signing certificate.
    /// </summary>
    public const string RepositorySigningCertificateRouteName = "repository-signing-certificate";

    /// <summary>
    /// A package's versions.
    /// </summary>
    public const string PackageVersionsRouteName = "package-versions";

    /// <summary>
    /// Downloading a package.
    /// </summary>
    public const string PackageDownloadRouteName = "package-download";

    /// <summary>
    /// Downloading a package's nuspec.
    /// </summary>
    public const string PackageDownloadManifestRouteName = "package-download-manifest";

    /// <summary>
    /// Downloading a package's README.
    /// </summary>
    public const string PackageDownloadReadmeRouteName = "package-download-readme";

    /// <summary>
    /// Downloading a package's icon.
    /// </summary>
    public const string PackageDownloadIconRouteName = "package-download-icon";

    /// <summary>
    /// Downloading a package's Markdown license.
    /// </summary>
    public const string PackageDownloadLicenseMarkDownRouteName = "package-download-license-md";

    /// <summary>
    /// Downloading a package's text license.
    /// </summary>
    public const string PackageDownloadLicenseTextRouteName = "package-download-license-txt";

    /// <summary>
    /// Downloading a symbol file.
    /// </summary>
    public const string SymbolDownloadRouteName = "symbol-download";

    /// <summary>
    /// Downloading a symbol file, with the symbol server's path prefix.
    /// </summary>
    public const string PrefixedSymbolDownloadRouteName = "prefixed-symbol-download";
}
