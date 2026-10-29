using Markdig;
using Microsoft.AspNetCore.Html;
using NuGet.Frameworks;
using NuGet.Versioning;
using NuGetFeed.Extensions;
using NuGetFeed.Models;
using NuGetFeed.PackageDetails;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NuGetFeed.Pages;

/// <summary>
/// A package's details page.
/// </summary>
public class PackageModel(IPackageDetailsClient packages, FeedConnection feed)
{
    private static readonly MarkdownPipeline MarkdownPipeline;

    private readonly IPackageDetailsClient _packages = packages ?? throw new ArgumentNullException(nameof(packages));
    private readonly FeedConnection _feed = feed ?? throw new ArgumentNullException(nameof(feed));

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

    public NuGetFeedPackageMetadata Package { get; private set; } = new();

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
    /// <param name="version">The version to show, or null to show the latest version.</param>
    public string PackageLink(string id, NuGetVersion? version = null)
    {
        var link = $"{PathBase}/packages/{Uri.EscapeDataString(id)}";

        return version == null ? link : $"{link}/{Uri.EscapeDataString(version.ToNormalizedString())}";
    }

    /// <summary>
    /// The commands that install the package, shown in tabs.
    /// </summary>
    public IReadOnlyList<InstallInstructions> GetInstallInstructions()
    {
        var id = Package.PackageId;
        var version = Version.ToNormalizedString();

        if (IsDotnetTemplate)
        {
            return [new InstallInstructions(".NET CLI", ">", [$"dotnet new --install {id}::{version}"])];
        }

        if (IsDotnetTool)
        {
            return [new InstallInstructions(".NET CLI", ">", [$"dotnet tool install --global {id} --version {version}"])];
        }

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
        var packages = await _packages.GetPackageMetadataAsync(id, cancellationToken);
        var listedPackages = packages.Where(p => p.IsListed()).ToList();

        NuGetFeedPackageMetadata? package = null;

        // Try to find the requested version.
        if (NuGetVersion.TryParse(version, out var requestedVersion))
        {
            package = packages.SingleOrDefault(p => p.ParseVersion() == requestedVersion);
        }

        // Otherwise try to display the latest version.
        package ??= listedPackages.OrderByDescending(p => p.ParseVersion()).FirstOrDefault();

        if (package == null)
        {
            Package = new NuGetFeedPackageMetadata { PackageId = id };
            Found = false;
            return;
        }

        Package = package;
        Version = Package.ParseVersion();

        var packageTypes = Package.PackageTypes ?? [];

        Found = true;
        IsDotnetTemplate = packageTypes.Any(t => t.Equals("Template", StringComparison.OrdinalIgnoreCase));
        IsDotnetTool = packageTypes.Any(t => t.Equals("DotnetTool", StringComparison.OrdinalIgnoreCase));
        LastUpdated = packages.Max(p => p.Published).UtcDateTime;
        TotalDownloads = packages.Sum(p => p.Downloads);

        var dependents = await _packages.GetPackageDependentsAsync(Package.PackageId, cancellationToken);

        UsedBy = dependents.Data;
        DependencyGroups = ToDependencyGroups(Package);
        Versions = ToVersions(listedPackages, Version);

        if (Package.HasReadme)
        {
            Readme = await GetReadmeHtmlStringOrNullAsync(Package.PackageId, Version, cancellationToken);
        }

        IconUrl = _feed.ToPublicUrl(Package.IconUrl);
        LicenseUrl = _feed.ToPublicUrl(Package.LicenseUrl);
        PackageDownloadUrl = _feed.ToPublicUrl(Package.PackageContentUrl);
    }

    private IReadOnlyList<DependencyGroupModel> ToDependencyGroups(NuGetFeedPackageMetadata package)
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
        if (targetFramework == null) return "All Frameworks";

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
        if (framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.NetCoreApp,
            StringComparison.OrdinalIgnoreCase))
        {
            frameworkName = (framework.Version.Major >= 5)
                ? ".NET"
                : ".NET Core";
        }
        else if (framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.NetStandard,
            StringComparison.OrdinalIgnoreCase))
        {
            frameworkName = ".NET Standard";
        }
        else if (framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.Net,
            StringComparison.OrdinalIgnoreCase))
        {
            frameworkName = ".NET Framework";
        }
        else
        {
            frameworkName = framework.Framework;
        }

        var frameworkVersion = (framework.Version.Build == 0)
            ? framework.Version.ToString(2)
            : framework.Version.ToString(3);

        return $"{frameworkName} {frameworkVersion}";
    }

    private IReadOnlyList<VersionModel> ToVersions(IReadOnlyList<NuGetFeedPackageMetadata> packages, NuGetVersion selectedVersion)
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

    private async Task<HtmlString?> GetReadmeHtmlStringOrNullAsync(
        string packageId,
        NuGetVersion packageVersion,
        CancellationToken cancellationToken)
    {
        string readme;
        using (var readmeStream = await _packages.GetPackageReadmeStreamOrNullAsync(packageId, packageVersion, cancellationToken))
        {
            if (readmeStream == null) return null;

            using var reader = new StreamReader(readmeStream);
            readme = await reader.ReadToEndAsync(cancellationToken);
        }

        var readmeHtml = Markdown.ToHtml(readme, MarkdownPipeline);
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
