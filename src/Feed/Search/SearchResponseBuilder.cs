using System;
using System.Collections.Generic;
using System.Linq;
using NuDepot.Core.Models;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Metadata;

namespace NuDepot.Feed.Search;

public class SearchResponseBuilder : ISearchResponseBuilder
{
    private readonly IUrlGenerator _url;

    public SearchResponseBuilder(IUrlGenerator url)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
    }

    public SearchResponse BuildSearch(IReadOnlyList<PackageRegistration> packageRegistrations, long totalHits, bool includeSemVer2)
    {
        var result = new List<SearchResult>();

        foreach (var packageRegistration in packageRegistrations)
        {
            var versions = packageRegistration.Packages.OrderByDescending(p => p.Version).ToList();
            var latest = versions.First();
            var iconUrl = latest.HasEmbeddedIcon
                ? _url.GetPackageIconDownloadUrl(latest.Id, latest.Version)
                : latest.IconUrlString;
            var licenseUrl = latest.HasEmbeddedLicense
                ? _url.GetPackageLicenseDownloadUrl(latest.Id, latest.Version, latest.LicenseIsMarkDown)
                : latest.LicenseUrlString;

            result.Add(new SearchResult
            {
                PackageId = latest.Id,
                Version = latest.Version.ToFullString(),
                Description = latest.Description,
                Authors = latest.Authors,
                IconUrl = iconUrl,
                LicenseUrl = licenseUrl,
                PackageTypes = (latest.PackageTypes ?? new List<PackageType>())
                    .Where(t => t.Name != null)
                    .Select(t => new SearchResultPackageType { Name = t.Name! })
                    .ToList(),
                ProjectUrl = latest.ProjectUrlString,
                RegistrationIndexUrl = _url.GetRegistrationIndexUrl(latest.Id, includeSemVer2),
                Summary = latest.Summary,
                Tags = latest.Tags,
                Title = latest.Title,
                TotalDownloads = versions.Sum(p => p.Downloads),
                Versions = versions
                    .Select(p => new SearchResultVersion
                    {
                        RegistrationLeafUrl = _url.GetRegistrationLeafUrl(p.Id, p.Version, includeSemVer2),
                        Version = p.Version.ToFullString(),
                        Downloads = p.Downloads,
                    })
                    .ToList(),
                Deprecation = latest.ToDeprecationOrNull(),
                Vulnerabilities = latest.ToVulnerabilitiesOrNull(packageRegistration.Vulnerabilities),
            });
        }

        return new SearchResponse
        {
            TotalHits = totalHits,
            Data = result,
            Context = SearchContext.Default(_url.GetPackageMetadataResourceUrl(includeSemVer2)),
        };
    }

    public AutocompleteResponse BuildAutocomplete(IReadOnlyList<string> data, long totalHits)
    {
        return new AutocompleteResponse
        {
            TotalHits = totalHits,
            Data = data,
            Context = AutocompleteContext.Default
        };
    }

    public DependentsResponse BuildDependents(IReadOnlyList<PackageDependent> packages)
    {
        return new DependentsResponse
        {
            TotalHits = packages.Count,
            Data = packages,
        };
    }
}
