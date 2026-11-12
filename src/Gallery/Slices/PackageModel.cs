using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Html;
using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.PackageDetails;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace NuDepot.Gallery.Slices;

/// <summary>
/// The model of <see cref="Package"/>, a package's details page.
/// </summary>
public class PackageModel(IPackageDetailsClient packages, FeedConnection feed)
{
    // A readme is written by the package's author, so its raw HTML isn't rendered: it could run script in the
    // gallery, as whoever reads it.
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private IPackageDetailsClient Packages { get; } = packages;
    private FeedConnection Feed { get; } = feed;

    /// <summary>
    /// The application's path base, which links start with.
    /// </summary>
    public string PathBase { get; set; } = string.Empty;

    /// <summary>
    /// Whether the package version exists and is listed.
    /// </summary>
    public bool Found { get; private set; }

    /// <summary>
    /// The package version's metadata.
    /// </summary>
    public NuDepotPackageMetadata Package { get; private set; } = new();

    /// <summary>
    /// The package's version. Only set if <see cref="Found"/>.
    /// </summary>
    public NuGetVersion Version { get; private set; } = new(0, 0, 0);

    /// <summary>
    /// Whether the package is a .NET template, installed with "dotnet new".
    /// </summary>
    public bool IsDotnetTemplate { get; private set; }

    /// <summary>
    /// Whether the package is a .NET tool, installed with "dotnet tool".
    /// </summary>
    public bool IsDotnetTool { get; private set; }

    /// <summary>
    /// Whether the package is a PowerShell module or script, installed with Install-PSResource.
    /// </summary>
    public bool IsPowerShellResource { get; private set; }

    /// <summary>
    /// When the package last had a version published.
    /// </summary>
    public DateTime LastUpdated { get; private set; }

    /// <summary>
    /// The downloads of every version of the package.
    /// </summary>
    public long TotalDownloads { get; private set; }

    /// <summary>
    /// The packages on the feed that depend on this one, most downloaded first.
    /// </summary>
    public IReadOnlyList<PackageDependent> UsedBy { get; private set; } = [];

    /// <summary>
    /// The package's dependencies, or null if the package wasn't found.
    /// </summary>
    public IReadOnlyList<DependencyGroupModel>? DependencyGroups { get; private set; }

    /// <summary>
    /// The package's listed versions, or null if the package wasn't found.
    /// </summary>
    public IReadOnlyList<VersionModel>? Versions { get; private set; }

    /// <summary>
    /// Every version of the package, listed or not, for a visitor who manages it. Empty for everyone else.
    /// </summary>
    public IReadOnlyList<VersionModel> AllVersions { get; private set; } = [];

    /// <summary>
    /// Who's visiting, and what they may manage. Null if nobody is signed in.
    /// </summary>
    public Visitor? Visitor { get; set; }

    /// <summary>
    /// Whether the visitor manages the package: they see its management section.
    /// </summary>
    public bool IsManaged => Found && Visitor?.CanManagePackages == true;

    /// <summary>
    /// The README, rendered from Markdown, or null if the package doesn't have one.
    /// </summary>
    public HtmlString? Readme { get; private set; }

    /// <summary>
    /// The URL of the package's icon, or null for the default icon.
    /// </summary>
    public string? IconUrl { get; private set; }

    /// <summary>
    /// The URL of the package's license, if it has one, and it's an HTTP or HTTPS URL.
    /// </summary>
    public string? LicenseUrl { get; private set; }

    /// <summary>
    /// The URL of the package's project, if it has one, and it's an HTTP or HTTPS URL.
    /// </summary>
    public string? ProjectUrl { get; private set; }

    /// <summary>
    /// The URL of the package's source code, if it has one, and it's an HTTP or HTTPS URL.
    /// </summary>
    public string? RepositoryUrl { get; private set; }

    /// <summary>
    /// The URL to download the package.
    /// </summary>
    public string? PackageDownloadUrl { get; private set; }

    /// <summary>
    /// The version's deprecation, if it's deprecated.
    /// </summary>
    public PackageDeprecation? Deprecation => Package.Deprecation;

    /// <summary>
    /// The known vulnerabilities that affect the version.
    /// </summary>
    public IReadOnlyList<PackageVulnerability> Vulnerabilities => Package.Vulnerabilities ?? [];

    /// <summary>
    /// Describes a vulnerability severity the way nuget.org does.
    /// </summary>
    public static string SeverityName(string severity) => severity switch
    {
        "1" => "moderate",
        "2" => "high",
        "3" => "critical",
        _ => "low",
    };

    /// <summary>
    /// Describes a deprecation reason the way nuget.org does.
    /// </summary>
    public static string DeprecationReasonText(string reason) => reason switch
    {
        "Legacy" => "is no longer maintained",
        "CriticalBugs" => "has critical bugs",
        _ => "is deprecated for other reasons",
    };

    /// <summary>
    /// A link to a package's details page.
    /// </summary>
    /// <param name="id">The package's ID.</param>
    /// <param name="version">The version to show, or null to show the latest version.</param>
    public string PackageLink(string id, NuGetVersion? version = null)
    {
        string link = $"{PathBase}/packages/{Uri.EscapeDataString(id)}";

        return version == null ? link : $"{link}/{Uri.EscapeDataString(version.ToNormalizedString())}";
    }

    /// <summary>
    /// The commands that install the package, shown in tabs.
    /// </summary>
    public IReadOnlyList<InstallInstructions> GetInstallInstructions()
    {
        string id = Package.PackageId;
        string version = Version.ToNormalizedString();

        if (IsDotnetTemplate)
            return [new InstallInstructions(".NET CLI", ">", [$"dotnet new --install {id}::{version}"])];

        if (IsDotnetTool)
            return [new InstallInstructions(".NET CLI", ">", [$"dotnet tool install --global {id} --version {version}"])];

        // PSResourceGet comes with PowerShell 7.4 and later. The repository is registered as on the upload page.
        if (IsPowerShellResource)
            return [new InstallInstructions("PowerShell", "PS>", [$"Install-PSResource -Name {id} -Version {version} -Repository NuDepot"])];

        return
        [
            new InstallInstructions(".NET CLI", ">", [$"dotnet add package {id} --version {version}"]),
            new InstallInstructions("PackageReference", "", [$"<PackageReference Include=\"{id}\" Version=\"{version}\" />"]),
            new InstallInstructions("Paket CLI", ">", [$"paket add {id} --version {version}"]),
            new InstallInstructions("Package Manager", "PM>", [$"Install-Package {id} -Version {version}"]),
        ];
    }

    /// <summary>
    /// Reads the package version's details from the feed.
    /// </summary>
    /// <param name="id">The package's ID.</param>
    /// <param name="version">The version to show, or null for the latest listed version.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>A task that completes when the model's properties are set.</returns>
    public Task LoadAsync(string id, string? version, CancellationToken cancellationToken)
        => LoadAsync(id, version, includeUnlisted: false, cancellationToken);

    /// <summary>
    /// Reads the package version's details from the feed.
    /// </summary>
    /// <param name="id">The package's ID.</param>
    /// <param name="version">The version to show, or null for the latest listed version.</param>
    /// <param name="includeUnlisted">Whether the visitor manages the package, so they see its unlisted versions too,
    /// in <see cref="AllVersions"/>, and a package whose versions are all unlisted.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>A task that completes when the model's properties are set.</returns>
    public async Task LoadAsync(string id, string? version, bool includeUnlisted, CancellationToken cancellationToken)
    {
        IReadOnlyList<NuDepotPackageMetadata> packages = await Packages.GetPackageMetadataAsync(id, cancellationToken);
        List<NuDepotPackageMetadata> listedPackages = [.. packages.Where(p => p.IsListed())];

        NuDepotPackageMetadata? package = null;

        // Try to find the requested version.
        if (NuGetVersion.TryParse(version, out NuGetVersion? requestedVersion))
            package = packages.SingleOrDefault(p => p.ParseVersion() == requestedVersion);

        // Otherwise try to display the latest version, which for a manager may be unlisted.
        package ??= (includeUnlisted && listedPackages.Count == 0 ? packages : listedPackages)
            .OrderByDescending(p => p.ParseVersion())
            .FirstOrDefault();

        if (package == null)
        {
            Package = new NuDepotPackageMetadata { PackageId = id };
            Found = false;
            return;
        }

        Package = package;
        Version = Package.ParseVersion();

        IReadOnlyList<string> packageTypes = Package.PackageTypes ?? [];

        Found = true;
        IsDotnetTemplate = packageTypes.Any(t => t.Equals("Template", StringComparison.OrdinalIgnoreCase));
        IsDotnetTool = packageTypes.Any(t => t.Equals("DotnetTool", StringComparison.OrdinalIgnoreCase));
        IsPowerShellResource = (Package.Tags ?? []).Any(t => t.Equals("PSModule", StringComparison.OrdinalIgnoreCase) || t.Equals("PSScript", StringComparison.OrdinalIgnoreCase));
        LastUpdated = packages.Max(p => p.Published).UtcDateTime;
        TotalDownloads = packages.Sum(p => p.Downloads);

        DependentsResponse dependents = await Packages.GetPackageDependentsAsync(Package.PackageId, cancellationToken);

        UsedBy = dependents.Data;
        DependencyGroups = ToDependencyGroups(Package);
        Versions = ToVersions(listedPackages, Version);
        AllVersions = includeUnlisted ? ToVersions(packages, Version) : [];

        if (Package.HasReadme)
            Readme = await GetReadmeHtmlStringOrNullAsync(Package.PackageId, Version, cancellationToken);

        IconUrl = Feed.ToPublicUrl(Package.IconUrl);
        LicenseUrl = SafeUrls.AbsoluteOrNull(Feed.ToPublicUrl(Package.LicenseUrl));
        ProjectUrl = SafeUrls.AbsoluteOrNull(Package.ProjectUrl);
        RepositoryUrl = SafeUrls.AbsoluteOrNull(Package.RepositoryUrl);
        PackageDownloadUrl = Feed.ToPublicUrl(Package.PackageContentUrl);
    }

    private IReadOnlyList<DependencyGroupModel> ToDependencyGroups(NuDepotPackageMetadata package)
    {
        return [.. (package.DependencyGroups ?? [])
            .Select(group => new DependencyGroupModel
            {
                Name = PrettifyTargetFramework(group.TargetFramework),
                Dependencies = [.. (group.Dependencies ?? [])
                    .Where(d => d.Id != null)
                    .Select(d => new DependencyModel
                    {
                        PackageId = d.Id,
                        VersionSpec = (d.Range != null)
                            ? VersionRange.Parse(d.Range).PrettyPrint()
                            : string.Empty
                    })]
            })];
    }

    private string PrettifyTargetFramework(string? targetFramework)
    {
        if (targetFramework == null)
            return "All Frameworks";

        NuGetFramework framework;
        try
        {
            framework = NuGetFramework.Parse(targetFramework);
        }
        catch (Exception)
        {
            return targetFramework;
        }

        string frameworkName;
        if (framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.NetCoreApp, StringComparison.OrdinalIgnoreCase))
            frameworkName = (framework.Version.Major >= 5) ? ".NET" : ".NET Core";
        else if (framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.NetStandard, StringComparison.OrdinalIgnoreCase))
            frameworkName = ".NET Standard";
        else if (framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.Net, StringComparison.OrdinalIgnoreCase))
            frameworkName = ".NET Framework";
        else
            frameworkName = framework.Framework;

        string frameworkVersion = (framework.Version.Build == 0)
            ? framework.Version.ToString(2)
            : framework.Version.ToString(3);

        return $"{frameworkName} {frameworkVersion}";
    }

    private IReadOnlyList<VersionModel> ToVersions(IReadOnlyList<NuDepotPackageMetadata> packages, NuGetVersion selectedVersion)
    {
        return [.. packages
            .Select(p => new VersionModel
            {
                Version = p.ParseVersion(),
                Downloads = p.Downloads,
                Selected = p.ParseVersion() == selectedVersion,
                Listed = p.IsListed(),
                LastUpdated = p.Published.UtcDateTime,
            })
            .OrderByDescending(m => m.Version)];
    }

    private async Task<HtmlString?> GetReadmeHtmlStringOrNullAsync(string packageId, NuGetVersion packageVersion, CancellationToken cancellationToken)
    {
        string readme;
        using (Stream? readmeStream = await Packages.GetPackageReadmeStreamOrNullAsync(packageId, packageVersion, cancellationToken))
        {
            if (readmeStream == null)
                return null;

            using StreamReader reader = new(readmeStream);
            readme = await reader.ReadToEndAsync(cancellationToken);
        }

        string readmeHtml = RenderReadme(readme);
        return new HtmlString(readmeHtml);
    }

    /// <summary>
    /// Renders a readme's Markdown as HTML, without its raw HTML, and without links or images whose URLs could run
    /// script, like "javascript:" ones.
    /// </summary>
    internal static string RenderReadme(string markdown)
    {
        MarkdownDocument document = Markdown.Parse(markdown, MarkdownPipeline);

        foreach (LinkInline link in document.Descendants<LinkInline>())
        {
            if (!SafeUrls.IsSafe(link.GetDynamicUrl?.Invoke() ?? link.Url))
            {
                link.GetDynamicUrl = null;
                link.Url = "#";
            }
        }

        foreach (AutolinkInline link in document.Descendants<AutolinkInline>())
        {
            if (!link.IsEmail && !SafeUrls.IsSafe(link.Url))
                link.Url = "#";
        }

        using StringWriter writer = new();
        HtmlRenderer renderer = new(writer);
        MarkdownPipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();

        return writer.ToString();
    }

    /// <summary>
    /// The package's dependencies for one target framework.
    /// </summary>
    public class DependencyGroupModel
    {
        /// <summary>
        /// The target framework's name, like ".NET 10.0".
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The dependencies.
        /// </summary>
        public required IReadOnlyList<DependencyModel> Dependencies { get; init; }
    }

    /// <summary>
    /// A dependency on another package.
    /// </summary>
    public class DependencyModel
    {
        /// <summary>
        /// The dependency's package ID.
        /// </summary>
        public required string PackageId { get; init; }

        /// <summary>
        /// The versions the package depends on, like "(>= 1.0.0)".
        /// </summary>
        public required string VersionSpec { get; init; }
    }

    /// <summary>
    /// One of the package's listed versions.
    /// </summary>
    public class VersionModel
    {
        /// <summary>
        /// The version.
        /// </summary>
        public required NuGetVersion Version { get; init; }

        /// <summary>
        /// The version's downloads.
        /// </summary>
        public long Downloads { get; init; }

        /// <summary>
        /// Whether this is the version the page shows.
        /// </summary>
        public bool Selected { get; init; }

        /// <summary>
        /// Whether the version is listed. Only managers see unlisted versions.
        /// </summary>
        public bool Listed { get; init; } = true;

        /// <summary>
        /// When the version was published.
        /// </summary>
        public DateTime LastUpdated { get; init; }
    }
}

/// <summary>
/// How to install a package with one tool.
/// </summary>
/// <param name="Name">The tool's name.</param>
/// <param name="Prefix">The prompt shown before the command.</param>
/// <param name="Content">The command's lines.</param>
public record InstallInstructions(string Name, string Prefix, IReadOnlyList<string> Content);
