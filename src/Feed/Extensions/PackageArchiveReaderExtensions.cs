using NuDepot.Feed.Entities;
using NuGet.Common;
using NuGet.Packaging;
using NuGetPackageType = NuGet.Packaging.Core.PackageType;

namespace NuDepot.Feed.Extensions;

public static class PackageArchiveReaderExtensions
{
    public static bool HasReadme(this PackageArchiveReader package)
        => !string.IsNullOrEmpty(package.NuspecReader.GetReadme());

    public static bool HasEmbeddedIcon(this PackageArchiveReader package)
        => !string.IsNullOrEmpty(package.NuspecReader.GetIcon());

    /// <summary>
    /// Indicates if the package has an embedded license file.
    /// </summary>
    /// <param name="package"></param>
    /// <returns></returns>
    public static bool HasEmbeddedLicense(this PackageArchiveReader package)
    {
        try
        {
            return package.NuspecReader.GetLicenseMetadata()?.Type == LicenseType.File;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if the package's license file format is MarkDown otherwise returns false
    /// </summary>
    /// <param name="package"></param>
    /// <returns></returns>
    public static bool GetLicenseIsMarkDown(this PackageArchiveReader package)
    {
        try
        {
            LicenseMetadata? licenseMetadata = package.NuspecReader.GetLicenseMetadata();
            if (licenseMetadata?.Type == LicenseType.File)
                return Path
                    .GetFileName(licenseMetadata.License)
                    .EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        }
        catch { }
        return false;
    }

    public async static Task<Stream> GetReadmeAsync(this PackageArchiveReader package, CancellationToken cancellationToken)
    {
        string? readmePath = package.NuspecReader.GetReadme();
        if (readmePath == null)
            throw new InvalidOperationException("Package does not have a readme!");

        return await package.GetStreamAsync(readmePath, cancellationToken);
    }

    public async static Task<Stream> GetIconAsync(this PackageArchiveReader package, CancellationToken cancellationToken)
    {
        string iconPath = package.NuspecReader.GetIcon()
            ?? throw new InvalidOperationException("Package does not have an icon!");

        return await package.GetStreamAsync(PathUtility.StripLeadingDirectorySeparators(iconPath), cancellationToken);
    }

    public async static Task<Stream?> GetLicenseAsync(this PackageArchiveReader package, CancellationToken cancellationToken)
    {
        LicenseMetadata? licenseMetadata = package.NuspecReader.GetLicenseMetadata();
        if (licenseMetadata != null &&
            licenseMetadata.Type == LicenseType.File)
            return await package.GetStreamAsync(PathUtility.StripLeadingDirectorySeparators(licenseMetadata.License), cancellationToken);
        return null;
    }

    public static Package GetPackageMetadata(this PackageArchiveReader packageReader)
    {
        NuspecReader nuspec = packageReader.NuspecReader;

        (Uri? repositoryUri, string? repositoryType) = GetRepositoryMetadata(nuspec);

        return new Package
        {
            Id = nuspec.GetId(),
            Version = nuspec.GetVersion(),
            Authors = ParseAuthors(nuspec.GetAuthors()),
            Description = nuspec.GetDescription(),
            HasReadme = packageReader.HasReadme(),
            HasEmbeddedIcon = packageReader.HasEmbeddedIcon(),
            HasEmbeddedLicense = packageReader.HasEmbeddedLicense(),
            IsPrerelease = nuspec.GetVersion().IsPrerelease,
            Language = nuspec.GetLanguage() ?? string.Empty,
            LicenseIsMarkDown = packageReader.GetLicenseIsMarkDown(),
            LicenseExpression = GetLicenseExpression(nuspec),
            ReleaseNotes = nuspec.GetReleaseNotes() ?? string.Empty,
            Listed = true,
            MinClientVersion = nuspec.GetMinClientVersion()?.ToNormalizedString() ?? string.Empty,
            PublishedAt = DateTime.UtcNow,
            RequireLicenseAcceptance = nuspec.GetRequireLicenseAcceptance(),
            SemVerLevel = GetSemVerLevel(nuspec),
            Summary = nuspec.GetSummary(),
            Title = nuspec.GetTitle(),
            IconUrl = ParseUri(nuspec.GetIconUrl()),
            LicenseUrl = ParseUri(nuspec.GetLicenseUrl()),
            ProjectUrl = ParseUri(nuspec.GetProjectUrl()),
            RepositoryUrl = repositoryUri,
            RepositoryType = repositoryType,
            Dependencies = GetDependencies(nuspec),
            Tags = ParseTags(nuspec.GetTags()),
            PackageTypes = GetPackageTypes(nuspec),
            TargetFrameworks = GetTargetFrameworks(packageReader),
        };
    }

    // Based off https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/SemVerLevelKey.cs
    private static SemVerLevel GetSemVerLevel(NuspecReader nuspec)
    {
        if (nuspec.GetVersion().IsSemVer2)
            return SemVerLevel.SemVer2;

        foreach (PackageDependencyGroup dependencyGroup in nuspec.GetDependencyGroups())
        {
            foreach (NuGet.Packaging.Core.PackageDependency dependency in dependencyGroup.Packages)
            {
                if (dependency.VersionRange.MinVersion != null && dependency.VersionRange.MinVersion.IsSemVer2
                    || dependency.VersionRange.MaxVersion != null && dependency.VersionRange.MaxVersion.IsSemVer2)
                    return SemVerLevel.SemVer2;
            }
        }

        return SemVerLevel.Unknown;
    }

    private static string? GetLicenseExpression(NuspecReader nuspec)
    {
        try
        {
            LicenseMetadata? license = nuspec.GetLicenseMetadata();

            return license?.Type == LicenseType.Expression ? license.License : null;
        }
        catch
        {
            return null;
        }
    }

    private static Uri? ParseUri(string? uriString)
    {
        if (string.IsNullOrEmpty(uriString))
            return null;

        return new Uri(uriString);
    }

    private static string[] ParseAuthors(string? authors)
    {
        if (string.IsNullOrEmpty(authors))
            return [];

        return authors.Split([',', ';', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static string[] ParseTags(string? tags)
    {
        if (string.IsNullOrEmpty(tags))
            return [];

        return tags.Split([',', ';', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static (Uri? repositoryUrl, string? repositoryType) GetRepositoryMetadata(NuspecReader nuspec)
    {
        NuGet.Packaging.Core.RepositoryMetadata repository = nuspec.GetRepositoryMetadata();

        if (string.IsNullOrEmpty(repository?.Url) ||
            !Uri.TryCreate(repository.Url, UriKind.Absolute, out Uri? repositoryUri))
            return (null, null);

        if (repositoryUri.Scheme != Uri.UriSchemeHttps)
            return (null, null);

        if (repository.Type?.Length > 100)
            throw new InvalidOperationException("Repository type must be less than or equal 100 characters");

        return (repositoryUri, repository.Type);
    }

    private static List<PackageDependency> GetDependencies(NuspecReader nuspec)
    {
        List<PackageDependency> dependencies = [];

        foreach (PackageDependencyGroup group in nuspec.GetDependencyGroups())
        {
            string targetFramework = group.TargetFramework.GetShortFolderName();

            if (!group.Packages.Any())
            {
                dependencies.Add(new PackageDependency
                {
                    Id = null,
                    VersionRange = null,
                    TargetFramework = targetFramework,
                });
            }

            foreach (NuGet.Packaging.Core.PackageDependency dependency in group.Packages)
            {
                dependencies.Add(new PackageDependency
                {
                    Id = dependency.Id,
                    VersionRange = dependency.VersionRange?.ToString(),
                    TargetFramework = targetFramework,
                });
            }
        }

        return dependencies;
    }

    private static List<PackageType> GetPackageTypes(NuspecReader nuspec)
    {
        List<PackageType> packageTypes = [.. nuspec
            .GetPackageTypes()
            .Select(t => new PackageType
            {
                Name = t.Name,
                Version = t.Version.ToString()
            })];

        // Default to the standard "dependency" package type if no types were found.
        if (packageTypes.Count == 0)
        {
            packageTypes.Add(new PackageType
            {
                Name = NuGetPackageType.Dependency.Name,
                Version = NuGetPackageType.Dependency.Version.ToString(),
            });
        }

        return packageTypes;
    }

    private static List<TargetFramework> GetTargetFrameworks(PackageArchiveReader packageReader)
    {
        List<TargetFramework> targetFrameworks = [.. packageReader
            .GetSupportedFrameworks()
            .Select(f => new TargetFramework
            {
                Moniker = f.GetShortFolderName()
            })];

        // Default to the "any" framework if no frameworks were found.
        if (targetFrameworks.Count == 0)
            targetFrameworks.Add(new TargetFramework { Moniker = "any" });

        return targetFrameworks;
    }
}
