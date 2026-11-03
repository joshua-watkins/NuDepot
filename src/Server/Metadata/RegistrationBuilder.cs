using NuGetFeed;
using NuGetFeed.Entities;
using NuGetFeed.Models;
using System;
using System.Linq;

namespace NuGetFeed.Metadata;

public class RegistrationBuilder
{
    private readonly IUrlGenerator _url;

    public RegistrationBuilder(IUrlGenerator url)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
    }

    public virtual NuGetFeedRegistrationIndexResponse BuildIndex(PackageRegistration registration)
    {
        var sortedPackages = registration.Packages.OrderBy(p => p.Version).ToList();
        var indexUrl = _url.GetRegistrationIndexUrl(registration.PackageId, registration.SemVer2);

        // TODO: Paging of registration items.
        // "Un-paged" example: https://api.nuget.org/v3/registration3/newtonsoft.json/index.json
        // Paged example: https://api.nuget.org/v3/registration3/fake/index.json
        return new NuGetFeedRegistrationIndexResponse
        {
            RegistrationIndexUrl = indexUrl,
            Type = RegistrationIndexResponse.DefaultType,
            Count = 1,
            TotalDownloads = registration.Packages.Sum(p => p.Downloads),
            Pages = new[]
            {
                new NuGetFeedRegistrationIndexPage
                {
                    RegistrationPageUrl = indexUrl,
                    Count = registration.Packages.Count,
                    Lower = sortedPackages.First().Version.ToNormalizedString().ToLowerInvariant(),
                    Upper = sortedPackages.Last().Version.ToNormalizedString().ToLowerInvariant(),
                    ItemsOrNull = sortedPackages.Select(p => ToRegistrationIndexPageItem(p, registration)).ToList(),
                }
            }
        };
    }

    public virtual RegistrationLeafResponse BuildLeaf(Package package, bool semVer2 = false, string? catalogLeafUrl = null)
    {
        var id = package.Id;
        var version = package.Version;

        return new RegistrationLeafResponse
        {
            Type = RegistrationLeafResponse.DefaultType,
            Listed = package.Listed,
            Published = ToUtc(package.PublishedAt),
            RegistrationLeafUrl = _url.GetRegistrationLeafUrl(id, version, semVer2),
            PackageContentUrl = _url.GetPackageDownloadUrl(id, version),
            RegistrationIndexUrl = _url.GetRegistrationIndexUrl(id, semVer2),
            CatalogEntryUrl = catalogLeafUrl,
        };
    }

    private NuGetFeedRegistrationIndexPageItem ToRegistrationIndexPageItem(Package package, PackageRegistration registration) =>
        new NuGetFeedRegistrationIndexPageItem
        {
            RegistrationLeafUrl = _url.GetRegistrationLeafUrl(package.Id, package.Version, registration.SemVer2),
            PackageContentUrl = _url.GetPackageDownloadUrl(package.Id, package.Version),
            PackageMetadata = new NuGetFeedPackageMetadata
            {
                CatalogLeafUrl = registration.GetCatalogLeafUrlOrNull(package),
                PackageId = package.Id,
                Version = package.Version.ToFullString(),
                Authors = string.Join(", ", package.Authors ?? Array.Empty<string>()),
                Description = package.Description,
                Deprecation = package.ToDeprecationOrNull(),
                Downloads = package.Downloads,
                HasReadme = package.HasReadme,
                IconUrl = package.HasEmbeddedIcon
                    ? _url.GetPackageIconDownloadUrl(package.Id, package.Version)
                    : package.IconUrlString,
                Language = package.Language,
                LicenseUrl = package.HasEmbeddedLicense
                    ? _url.GetPackageLicenseDownloadUrl(package.Id, package.Version, package.LicenseIsMarkDown)
                    : package.LicenseUrlString,
                LicenseExpression = package.LicenseExpression,
                Listed = package.Listed,
                MinClientVersion = package.MinClientVersion,
                ReadmeUrl = package.HasReadme ? _url.GetPackageReadmeDownloadUrl(package.Id, package.Version) : null,
                ReleaseNotes = package.ReleaseNotes,
                PackageContentUrl = _url.GetPackageDownloadUrl(package.Id, package.Version),
                PackageTypes = package.PackageTypes.Select(t => t.Name).OfType<string>().ToList(),
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
