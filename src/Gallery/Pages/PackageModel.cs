using Markdig;
using Microsoft.AspNetCore.Html;
using NuDepot.Core.Extensions;
using NuDepot.Core.Models;
using NuDepot.Core.PackageDetails;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace NuDepot.Gallery.Pages;

/// <summary>
/// A package's details page.
/// </summary>
public class PackageModel(IPackageDetailsClient packages, FeedConnection feed)
{
    private static readonly MarkdownPipeline MarkdownPipeline;

    private IPackageDetailsClient Packages { get; } = packages;
    private FeedConnection Feed { get; } = feed;

    static PackageModel()
    {
        MarkdownPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();
    }

    /// <summary>
    /// The application's path base, which links start with.
    /// </summary>
    public string PathBase { get; set; } = string.Empty;

    public bool Found { get; private set; }

    public NuDepotPackageMetadata Package { get; private set; } = new();

    /// <summary>
    /// The package's version. Only set if <see cref="Found"/>.
    /// </summary>
    public NuGetVersion Version { get; private set; } = new(0, 0, 0);

    public bool IsDotnetTemplate { get; private set; }
    public bool IsDotnetTool { get; private set; }
    public DateTime LastUpdated { get; private set; }
    public long TotalDownloads { get; private set; }

    public IReadOnlyList<PackageDependent> UsedBy { get; set; } = [];

    /// <summary>
    /// The package's dependencies, or null if the package wasn't found.
    /// </summary>
    public IReadOnlyList<DependencyGroupModel>? DependencyGroups { get; private set; }

    /// <summary>
    /// The package's listed versions, or null if the package wasn't found.
    /// </summary>
    public IReadOnlyList<VersionModel>? Versions { get; private set; }

    public HtmlString? Readme { get; private set; }

    public string? IconUrl { get; private set; }
    public string? LicenseUrl { get; private set; }
    public string? PackageDownloadUrl { get; private set; }

    public PackageDeprecation? Deprecation => Package.Deprecation;
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

        return
        [
            new InstallInstructions(".NET CLI", ">", [$"dotnet add package {id} --version {version}"]),
            new InstallInstructions("PackageReference", "", [$"<PackageReference Include=\"{id}\" Version=\"{version}\" />"]),
            new InstallInstructions("Paket CLI", ">", [$"paket add {id} --version {version}"]),
            new InstallInstructions("Package Manager", "PM>", [$"Install-Package {id} -Version {version}"]),
        ];
    }

    public async Task OnGetAsync(string id, string? version, CancellationToken cancellationToken)
    {
        IReadOnlyList<NuDepotPackageMetadata> packages = await Packages.GetPackageMetadataAsync(id, cancellationToken);
        List<NuDepotPackageMetadata> listedPackages = [.. packages.Where(p => p.IsListed())];

        NuDepotPackageMetadata? package = null;

        // Try to find the requested version.
        if (NuGetVersion.TryParse(version, out NuGetVersion? requestedVersion))
            package = packages.SingleOrDefault(p => p.ParseVersion() == requestedVersion);

        // Otherwise try to display the latest version.
        package ??= listedPackages
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
        LastUpdated = packages.Max(p => p.Published).UtcDateTime;
        TotalDownloads = packages.Sum(p => p.Downloads);

        DependentsResponse dependents = await Packages.GetPackageDependentsAsync(Package.PackageId, cancellationToken);

        UsedBy = dependents.Data;
        DependencyGroups = ToDependencyGroups(Package);
        Versions = ToVersions(listedPackages, Version);

        if (Package.HasReadme)
            Readme = await GetReadmeHtmlStringOrNullAsync(Package.PackageId, Version, cancellationToken);

        IconUrl = Feed.ToPublicUrl(Package.IconUrl);
        LicenseUrl = Feed.ToPublicUrl(Package.LicenseUrl);
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

        string readmeHtml = Markdown.ToHtml(readme, MarkdownPipeline);
        return new HtmlString(readmeHtml);
    }

    public class DependencyGroupModel
    {
        public required string Name { get; init; }
        public required IReadOnlyList<DependencyModel> Dependencies { get; init; }
    }

    public class DependencyModel
    {
        public required string PackageId { get; init; }
        public required string VersionSpec { get; init; }
    }

    public class VersionModel
    {
        public required NuGetVersion Version { get; init; }
        public long Downloads { get; init; }
        public bool Selected { get; init; }
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
