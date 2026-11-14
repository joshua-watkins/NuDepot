using NuDepot.Core.Models;
using NuDepot.Feed.Entities;
using NuGet.Versioning;

namespace NuDepot.Feed.Metadata;

/// <summary>
/// Builds the package metadata (registration) resource's responses.
/// </summary>
/// <param name="url">Generates the feed's URLs.</param>
internal class RegistrationBuilder(IUrlGenerator url)
{
    /// <summary>
    /// The most versions a registration index lists itself. A package with more is split into pages of
    /// <see cref="PageSize"/> versions, each its own document, as on nuget.org.
    /// </summary>
    public const int MaxInlinedVersions = 128;

    /// <summary>
    /// How many versions each page of a package with more than <see cref="MaxInlinedVersions"/> lists.
    /// </summary>
    public const int PageSize = 64;

    private IUrlGenerator Url { get; } = url;

    /// <summary>
    /// Builds a package's registration index. Up to <see cref="MaxInlinedVersions"/> versions are listed in one
    /// page in the index. More are split into pages of <see cref="PageSize"/>, which the index links to.
    /// </summary>
    /// <param name="registration">The package's versions.</param>
    /// <returns>The registration index.</returns>
    public virtual NuDepotRegistrationIndexResponse BuildIndex(PackageRegistration registration)
    {
        string indexUrl = Url.GetRegistrationIndexUrl(registration.PackageId, registration.SemVer2);
        bool inline = registration.Packages.Count <= MaxInlinedVersions;
        List<Package[]> pages = Paginate(registration);

        return new NuDepotRegistrationIndexResponse
        {
            RegistrationIndexUrl = indexUrl,
            Type = RegistrationIndexResponse.DefaultType,
            Count = pages.Count,
            TotalDownloads = registration.Packages.Sum(p => p.Downloads),
            Pages = [.. pages.Select(page =>
            {
                (string lower, string upper) = Bounds(page);
                return new NuDepotRegistrationIndexPage
                {
                    // A page listed in the index is identified within it.
                    RegistrationPageUrl = inline
                        ? $"{indexUrl}#page/{lower}/{upper}"
                        : Url.GetRegistrationPageUrl(registration.PackageId, lower, upper, registration.SemVer2),
                    Count = page.Length,
                    Lower = lower,
                    Upper = upper,
                    ItemsOrNull = inline ? [.. page.Select(p => ToRegistrationIndexPageItem(p, registration))] : null,
                };
            })],
        };
    }

    /// <summary>
    /// Builds a page of a package's registration, which the index links to because the package has more than
    /// <see cref="MaxInlinedVersions"/> versions.
    /// </summary>
    /// <param name="registration">The package's versions.</param>
    /// <param name="lower">The page's lowest version, as the index's link says it.</param>
    /// <param name="upper">The page's highest version, as the index's link says it.</param>
    /// <returns>The page, or null if the index doesn't link to a page with those versions.</returns>
    public virtual NuDepotRegistrationPageResponse? BuildPageOrNull(PackageRegistration registration, NuGetVersion lower, NuGetVersion upper)
    {
        if (registration.Packages.Count <= MaxInlinedVersions)
            return null;

        Package[]? page = Paginate(registration).FirstOrDefault(p => p[0].Version == lower && p[^1].Version == upper);
        if (page == null)
            return null;

        (string lowerString, string upperString) = Bounds(page);
        return new NuDepotRegistrationPageResponse
        {
            RegistrationPageUrl = Url.GetRegistrationPageUrl(registration.PackageId, lowerString, upperString, registration.SemVer2),
            RegistrationIndexUrl = Url.GetRegistrationIndexUrl(registration.PackageId, registration.SemVer2),
            Count = page.Length,
            Lower = lowerString,
            Upper = upperString,
            ItemsOrNull = [.. page.Select(p => ToRegistrationIndexPageItem(p, registration))],
        };
    }

    // The package's versions in order, in one page if there are few enough, or else in pages of PageSize.
    private static List<Package[]> Paginate(PackageRegistration registration)
    {
        Package[] sorted = [.. registration.Packages.OrderBy(p => p.Version)];

        return sorted.Length <= MaxInlinedVersions ? [sorted] : [.. sorted.Chunk(PageSize)];
    }

    // A page's lowest and highest versions, normalized and lowercased, without build metadata.
    private static (string Lower, string Upper) Bounds(Package[] page)
        => (page[0].Version.ToNormalizedString().ToLowerInvariant(), page[^1].Version.ToNormalizedString().ToLowerInvariant());

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
                // The catalog doesn't have versions a mirror lists that the feed hasn't downloaded yet, so their
                // entries come from their registration leaves.
                CatalogLeafUrl = registration.GetCatalogLeafUrlOrNull(package)
                    ?? Url.GetRegistrationLeafUrl(package.Id, package.Version, registration.SemVer2),
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
