using Microsoft.AspNetCore.Http;
using NuDepot.Core.Models;
using NuDepot.Core.Search;

namespace NuDepot.Gallery.Pages;

/// <summary>
/// The search page.
/// </summary>
public class IndexModel(ISearchClient search, FeedConnection feed)
{
    private ISearchClient Search { get; } = search;
    private FeedConnection Feed { get; } = feed;

    /// <summary>
    /// The most packages on a page of results.
    /// </summary>
    public const int ResultsPerPage = 20;

    /// <summary>
    /// The package types to filter by, keyed by the value the feed understands.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PackageTypes { get; } = new Dictionary<string, string>()
    {
        { "any", "Any" },
        { "dependency", "Dependency" },
        { "dotnettool", ".NET tool" },
        { "dotnettemplate", ".NET template" },
    };

    /// <summary>
    /// The target frameworks to filter by, keyed by the value the feed understands. "divider" and "header" keys separate the groups.
    /// </summary>
    public static IReadOnlyDictionary<string, string> TargetFrameworks { get; } = new Dictionary<string, string>()
    {
        { "any", "Any" },

        { "divider1", "-" },
        { "header1", ".NET" },

        { "net10.0", ".NET 10.0" },
        { "net9.0", ".NET 9.0" },
        { "net8.0", ".NET 8.0" },
        { "net7.0", ".NET 7.0" },
        { "net6.0", ".NET 6.0" },
        { "net5.0", ".NET 5.0" },

        { "divider2", "-" },
        { "header2", ".NET Standard" },

        { "netstandard2.1", ".NET Standard 2.1" },
        { "netstandard2.0", ".NET Standard 2.0" },
        { "netstandard1.6", ".NET Standard 1.6" },
        { "netstandard1.5", ".NET Standard 1.5" },
        { "netstandard1.4", ".NET Standard 1.4" },
        { "netstandard1.3", ".NET Standard 1.3" },
        { "netstandard1.2", ".NET Standard 1.2" },
        { "netstandard1.1", ".NET Standard 1.1" },
        { "netstandard1.0", ".NET Standard 1.0" },

        { "divider3", "-" },
        { "header3", ".NET Core" },

        { "netcoreapp3.1", ".NET Core 3.1" },
        { "netcoreapp3.0", ".NET Core 3.0" },
        { "netcoreapp2.2", ".NET Core 2.2" },
        { "netcoreapp2.1", ".NET Core 2.1" },
        { "netcoreapp1.1", ".NET Core 1.1" },
        { "netcoreapp1.0", ".NET Core 1.0" },

        { "divider4", "-" },
        { "header4", ".NET Framework" },

        { "net48", ".NET Framework 4.8" },
        { "net472", ".NET Framework 4.7.2" },
        { "net471", ".NET Framework 4.7.1" },
        { "net463", ".NET Framework 4.6.3" },
        { "net462", ".NET Framework 4.6.2" },
        { "net461", ".NET Framework 4.6.1" },
        { "net46", ".NET Framework 4.6" },
        { "net452", ".NET Framework 4.5.2" },
        { "net451", ".NET Framework 4.5.1" },
        { "net45", ".NET Framework 4.5" },
        { "net403", ".NET Framework 4.0.3" },
        { "net4", ".NET Framework 4" },
        { "net35", ".NET Framework 3.5" },
        { "net2", ".NET Framework 2" },
        { "net11", ".NET Framework 1.1" },
    };

    /// <summary>
    /// The "q" query parameter.
    /// </summary>
    public string? Query { get; set; }

    /// <summary>
    /// The "p" query parameter, starting at 1.
    /// </summary>
    public int PageIndex { get; set; } = 1;

    /// <summary>
    /// The "packageType" query parameter, or "any".
    /// </summary>
    public string PackageType { get; set; } = "any";

    /// <summary>
    /// The "framework" query parameter, or "any".
    /// </summary>
    public string Framework { get; set; } = "any";

    /// <summary>
    /// The "prerelease" query parameter: whether to include prerelease versions.
    /// </summary>
    public bool Prerelease { get; set; } = true;

    /// <summary>
    /// The application's path base, which links start with.
    /// </summary>
    public string PathBase { get; set; } = string.Empty;

    /// <summary>
    /// The page of matching packages.
    /// </summary>
    public IReadOnlyList<SearchResult> Packages { get; private set; } = [];

    /// <summary>
    /// Searches the feed for the page of packages.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>A task that completes when <see cref="Packages"/> is set.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        string? packageType = PackageType == "any" ? null : PackageType;
        string? framework = Framework == "any" ? null : Framework;

        SearchResponse search = await Search.SearchAsync(
            Query,
            skip: (PageIndex - 1) * ResultsPerPage,
            take: ResultsPerPage,
            includePrerelease: Prerelease,
            includeSemVer2: true,
            packageType,
            framework,
            cancellationToken);

        Packages = search.Data ?? [];

        foreach (SearchResult package in Packages)
        {
            package.IconUrl = Feed.ToPublicUrl(package.IconUrl);
        }
    }

    /// <summary>
    /// A link to this page with the current search, changing the given parameters.
    /// </summary>
    public string SearchLink(string? packageType = null, string? framework = null, bool? prerelease = null, int? page = null)
    {
        // Use model's value if the argument was skipped.
        packageType ??= PackageType;
        framework ??= Framework;
        prerelease ??= Prerelease;
        page ??= PageIndex;

        // Leave out query parameters that have their default value.
        List<KeyValuePair<string, string?>> parameters = [];
        if (!string.IsNullOrEmpty(Query))
            parameters.Add(new("q", Query));
        if (page != 1)
            parameters.Add(new("p", page.Value.ToString()));
        if (packageType != "any")
            parameters.Add(new("packageType", packageType));
        if (framework != "any")
            parameters.Add(new("framework", framework));
        if (prerelease != true)
            parameters.Add(new("prerelease", "False"));

        return PathBase + "/" + QueryString.Create(parameters).ToUriComponent();
    }
}
