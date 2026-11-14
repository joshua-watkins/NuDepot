using NuDepot.Core.Models;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Metadata;

namespace NuDepot.Feed.Search;

/// <summary>
/// Builds the search resource's responses.
/// </summary>
/// <param name="url">Generates the feed's URLs.</param>
internal class SearchResponseBuilder(IUrlGenerator url) : ISearchResponseBuilder
{
    private IUrlGenerator Url { get; } = url;

    /// <inheritdoc />
    public SearchResponse BuildSearch(IReadOnlyList<PackageRegistration> packageRegistrations, long totalHits, bool includeSemVer2)
    {
        List<SearchResult> result = [];

        foreach (PackageRegistration packageRegistration in packageRegistrations)
        {
            List<Package> versions = [.. packageRegistration.Packages.OrderByDescending(p => p.Version)];
            Package latest = versions.First();
            string iconUrl = latest.HasEmbeddedIcon
                ? Url.GetPackageIconDownloadUrl(latest.Id, latest.Version)
                : latest.IconUrlString;
            string licenseUrl = latest.HasEmbeddedLicense
                ? Url.GetPackageLicenseDownloadUrl(latest.Id, latest.Version, latest.LicenseIsMarkDown)
                : latest.LicenseUrlString;

            result.Add(new SearchResult
            {
                PackageId = latest.Id,
                Version = latest.Version.ToFullString(),
                Description = latest.Description,
                Authors = latest.Authors,
                IconUrl = iconUrl,
                LicenseUrl = licenseUrl,
                PackageTypes = [.. (latest.PackageTypes ?? [])
                    .Where(t => t.Name != null)
                    .Select(t => new SearchResultPackageType { Name = t.Name! })],
                ProjectUrl = latest.ProjectUrlString,
                RegistrationIndexUrl = Url.GetRegistrationIndexUrl(latest.Id, includeSemVer2),
                Summary = latest.Summary,
                Tags = latest.Tags,
                Title = latest.Title,
                TotalDownloads = versions.Sum(p => p.Downloads),
                Versions = [.. versions
                    .Select(p => new SearchResultVersion
                    {
                        RegistrationLeafUrl = Url.GetRegistrationLeafUrl(p.Id, p.Version, includeSemVer2),
                        Version = p.Version.ToFullString(),
                        Downloads = p.Downloads,
                    })],
                Deprecation = latest.ToDeprecationOrNull(),
                Vulnerabilities = latest.ToVulnerabilitiesOrNull(packageRegistration.Vulnerabilities),
            });
        }

        return new SearchResponse
        {
            TotalHits = totalHits,
            Data = result,
            Context = SearchContext.Default(Url.GetPackageMetadataResourceUrl(includeSemVer2)),
        };
    }

    /// <inheritdoc />
    public AutocompleteResponse BuildAutocomplete(IReadOnlyList<string> data, long totalHits)
    {
        return new AutocompleteResponse
        {
            TotalHits = totalHits,
            Data = data,
            Context = AutocompleteContext.Default
        };
    }

    /// <inheritdoc />
    public DependentsResponse BuildDependents(IReadOnlyList<PackageDependent> packages)
    {
        return new DependentsResponse
        {
            TotalHits = packages.Count,
            Data = packages,
        };
    }
}
