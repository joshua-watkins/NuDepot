using NuDepot.Core.Models;
using NuDepot.Feed.Entities;
using NuGet.Versioning;

namespace NuDepot.Feed.Metadata;

/// <summary>
/// Builds the package metadata (registration) resource's responses.
/// </summary>
/// <param name="url">Generates the feed's URLs.</param>
public class RegistrationBuilder(IUrlGenerator url)
{
    private IUrlGenerator Url { get; } = url;

    /// <summary>
    /// Builds a package's registration index, with every version inlined in one page.
    /// </summary>
    /// <param name="registration">The package's versions.</param>
    /// <returns>The registration index.</returns>
    public virtual NuDepotRegistrationIndexResponse BuildIndex(PackageRegistration registration)
    {
        List<Package> sortedPackages = [.. registration.Packages.OrderBy(p => p.Version)];
        string indexUrl = Url.GetRegistrationIndexUrl(registration.PackageId, registration.SemVer2);

        // TODO: Paging of registration items.
        // "Un-paged" example: https://api.nuget.org/v3/registration3/newtonsoft.json/index.json
        // Paged example: https://api.nuget.org/v3/registration3/fake/index.json
        return new NuDepotRegistrationIndexResponse
        {
            RegistrationIndexUrl = indexUrl,
            Type = RegistrationIndexResponse.DefaultType,
            Count = 1,
            TotalDownloads = registration.Packages.Sum(p => p.Downloads),
            Pages =
            [
                new NuDepotRegistrationIndexPage
                {
                    RegistrationPageUrl = indexUrl,
                    Count = registration.Packages.Count,
                    Lower = sortedPackages.First().Version.ToNormalizedString().ToLowerInvariant(),
                    Upper = sortedPackages.Last().Version.ToNormalizedString().ToLowerInvariant(),
                    ItemsOrNull = [.. sortedPackages.Select(p => ToRegistrationIndexPageItem(p, registration))],
                }
            ]
        };
    }

    /// <summary>
    /// Builds a package version's registration leaf.
    /// </summary>
    /// <param name="package">The package version.</param>
    /// <param name="semVer2">Whether the leaf is in the hive that includes SemVer 2.0.0 packages.</param>
    /// <param name="catalogLeafUrl">The URL of the version's latest catalog leaf, if the catalog has it.</param>
    /// <returns>The registration leaf.</returns>
    public virtual RegistrationLeafResponse BuildLeaf(Package package, bool semVer2 = false, string? catalogLeafUrl = null)
    {
        string id = package.Id;
        NuGetVersion version = package.Version;

        return new RegistrationLeafResponse
        {
            Type = RegistrationLeafResponse.DefaultType,
            Listed = package.Listed,
            Published = ToUtc(package.PublishedAt),
            RegistrationLeafUrl = Url.GetRegistrationLeafUrl(id, version, semVer2),
            PackageContentUrl = Url.GetPackageDownloadUrl(id, version),
            RegistrationIndexUrl = Url.GetRegistrationIndexUrl(id, semVer2),
            CatalogEntryUrl = catalogLeafUrl,
        };
    }

    private NuDepotRegistrationIndexPageItem ToRegistrationIndexPageItem(Package package, PackageRegistration registration) =>
        new()
        {
            RegistrationLeafUrl = Url.GetRegistrationLeafUrl(package.Id, package.Version, registration.SemVer2),
            PackageContentUrl = Url.GetPackageDownloadUrl(package.Id, package.Version),
            PackageMetadata = new NuDepotPackageMetadata
            {
                CatalogLeafUrl = registration.GetCatalogLeafUrlOrNull(package),
                PackageId = package.Id,
                Version = package.Version.ToFullString(),
                Authors = string.Join(", ", package.Authors ?? []),
                Description = package.Description,
                Deprecation = package.ToDeprecationOrNull(),
                Downloads = package.Downloads,
                HasReadme = package.HasReadme,
                IconUrl = package.HasEmbeddedIcon
                    ? Url.GetPackageIconDownloadUrl(package.Id, package.Version)
                    : package.IconUrlString,
                Language = package.Language,
                LicenseUrl = package.HasEmbeddedLicense
                    ? Url.GetPackageLicenseDownloadUrl(package.Id, package.Version, package.LicenseIsMarkDown)
                    : package.LicenseUrlString,
                LicenseExpression = package.LicenseExpression,
                Listed = package.Listed,
                MinClientVersion = package.MinClientVersion,
                ReadmeUrl = package.HasReadme ? Url.GetPackageReadmeDownloadUrl(package.Id, package.Version) : null,
                ReleaseNotes = package.ReleaseNotes,
                PackageContentUrl = Url.GetPackageDownloadUrl(package.Id, package.Version),
                PackageTypes = [.. package.PackageTypes.Select(t => t.Name).OfType<string>()],
                ProjectUrl = package.ProjectUrlString,
                RepositoryUrl = package.RepositoryUrlString,
                RepositoryType = package.RepositoryType,
                Published = ToUtc(package.PublishedAt),
                RequireLicenseAcceptance = package.RequireLicenseAcceptance,
                Summary = package.Summary,
                Tags = package.Tags,
                Title = package.Title,
                DependencyGroups = package.ToDependencyGroups(),
                Vulnerabilities = package.ToVulnerabilitiesOrNull(registration.Vulnerabilities),
            },
        };

    // Packages are published in UTC, but the database doesn't keep the date's kind.
    private static DateTimeOffset ToUtc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
