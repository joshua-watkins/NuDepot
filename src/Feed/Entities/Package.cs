using NuGet.Versioning;

namespace NuDepot.Feed.Entities;

// See NuGetGallery's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/Package.cs
// The properties' nullability matches the database schema: Entity Framework reads it to decide which columns are required.
public class Package
{
    public int Key { get; set; }

    public string Id { get; set; } = null!;

    public NuGetVersion Version
    {
        get
        {
            // Favor the original version string as it contains more information.
            // Packages uploaded with older versions of NuDepot may not have the original version string.
            return NuGetVersion.Parse(OriginalVersionString ?? NormalizedVersionString);
        }

        set
        {
            NormalizedVersionString = value
                .ToNormalizedString()
                .ToLowerInvariant();
            OriginalVersionString = value.OriginalVersion;
        }
    }

    public string[]? Authors { get; set; }
    public string? Description { get; set; }
    public long Downloads { get; set; }
    public bool HasReadme { get; set; }
    public bool HasEmbeddedIcon { get; set; }
    /// <summary>
    /// Indicates if the license is embedded in the package
    /// </summary>
    public bool HasEmbeddedLicense { get; set; }

    public bool IsPrerelease { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? Language { get; set; }

    /// <summary>
    /// Indicates if the license format is MarkDown (true) or Text (false)
    /// </summary>
    public bool LicenseIsMarkDown { get; set; }
    public bool Listed { get; set; }
    public string? MinClientVersion { get; set; }
    public DateTime PublishedAt { get; set; }
    public bool RequireLicenseAcceptance { get; set; }
    public SemVerLevel SemVerLevel { get; set; }
    public string? Summary { get; set; }
    public string? Title { get; set; }

    public Uri? IconUrl { get; set; }
    public Uri? LicenseUrl { get; set; }
    public Uri? ProjectUrl { get; set; }

    public Uri? RepositoryUrl { get; set; }
    public string? RepositoryType { get; set; }

    public string[]? Tags { get; set; }

    /// <summary>
    /// Used for optimistic concurrency.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    public List<PackageDependency> Dependencies { get; set; } = [];
    public List<PackageType> PackageTypes { get; set; } = [];
    public List<TargetFramework> TargetFrameworks { get; set; } = [];

    public string NormalizedVersionString { get; set; } = null!;
    public string? OriginalVersionString { get; set; }

    /// <summary>
    /// The package's SPDX license expression, if it uses one instead of a license file or URL.
    /// </summary>
    public string? LicenseExpression { get; set; }

    /// <summary>
    /// The base64-encoded SHA-512 hash of the stored package (.nupkg).
    /// </summary>
    public string? PackageHash { get; set; }

    /// <summary>
    /// The size of the stored package (.nupkg) in bytes.
    /// </summary>
    public long PackageSize { get; set; }

    /// <summary>
    /// Whether the stored package carries this server's repository signature.
    /// </summary>
    public bool IsRepositorySigned { get; set; }

    /// <summary>
    /// Why the package is deprecated: "Legacy", "CriticalBugs" or "Other".
    /// Empty or null if the package isn't deprecated.
    /// </summary>
    public string[]? DeprecationReasons { get; set; }
    public string? DeprecationMessage { get; set; }
    public string? AlternatePackageId { get; set; }
    public string? AlternatePackageVersionRange { get; set; }

    public bool IsDeprecated => DeprecationReasons?.Length > 0;


    public string IconUrlString => IconUrl?.AbsoluteUri ?? string.Empty;
    public string LicenseUrlString => LicenseUrl?.AbsoluteUri ?? string.Empty;
    public string ProjectUrlString => ProjectUrl?.AbsoluteUri ?? string.Empty;
    public string RepositoryUrlString => RepositoryUrl?.AbsoluteUri ?? string.Empty;
}
