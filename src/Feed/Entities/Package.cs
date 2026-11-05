using NuGet.Versioning;

namespace NuDepot.Feed.Entities;

// See NuGetGallery's: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Entities/Package.cs
// The properties' nullability matches the database schema: Entity Framework reads it to decide which columns are required.
/// <summary>
/// A package version on the feed.
/// </summary>
public class Package
{
    /// <summary>
    /// The row's key.
    /// </summary>
    public int Key { get; set; }

    /// <summary>
    /// The package's ID.
    /// </summary>
    public string Id { get; set; } = null!;

    /// <summary>
    /// The package's version. Setting it sets <see cref="NormalizedVersionString"/> and <see cref="OriginalVersionString"/>.
    /// </summary>
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

    /// <summary>
    /// The package's authors.
    /// </summary>
    public string[]? Authors { get; set; }

    /// <summary>
    /// The package's description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The version's total downloads.
    /// </summary>
    public long Downloads { get; set; }

    /// <summary>
    /// Whether the package has a README.
    /// </summary>
    public bool HasReadme { get; set; }

    /// <summary>
    /// Whether the package has an icon file.
    /// </summary>
    public bool HasEmbeddedIcon { get; set; }

    /// <summary>
    /// Indicates if the license is embedded in the package
    /// </summary>
    public bool HasEmbeddedLicense { get; set; }

    /// <summary>
    /// Whether the version is a prerelease.
    /// </summary>
    public bool IsPrerelease { get; set; }

    /// <summary>
    /// The package's release notes.
    /// </summary>
    public string? ReleaseNotes { get; set; }

    /// <summary>
    /// The package's language, like "en-US".
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Indicates if the license format is MarkDown (true) or Text (false)
    /// </summary>
    public bool LicenseIsMarkDown { get; set; }

    /// <summary>
    /// Whether the version is listed. Unlisted versions don't appear in searches, but can still be installed.
    /// </summary>
    public bool Listed { get; set; }

    /// <summary>
    /// The oldest NuGet client that can install the package.
    /// </summary>
    public string? MinClientVersion { get; set; }

    /// <summary>
    /// When the version was pushed, in UTC.
    /// </summary>
    public DateTime PublishedAt { get; set; }

    /// <summary>
    /// Whether users must accept the license to install the package.
    /// </summary>
    public bool RequireLicenseAcceptance { get; set; }

    /// <summary>
    /// Whether the version, or one of its dependencies, needs SemVer 2.0.0.
    /// </summary>
    public SemVerLevel SemVerLevel { get; set; }

    /// <summary>
    /// The package's summary.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// The package's title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The URL of the package's icon, if it links to one.
    /// </summary>
    public Uri? IconUrl { get; set; }

    /// <summary>
    /// The URL of the package's license, if it links to one.
    /// </summary>
    public Uri? LicenseUrl { get; set; }

    /// <summary>
    /// The URL of the package's project.
    /// </summary>
    public Uri? ProjectUrl { get; set; }

    /// <summary>
    /// The URL of the package's source repository.
    /// </summary>
    public Uri? RepositoryUrl { get; set; }

    /// <summary>
    /// The type of the package's source repository, like "git".
    /// </summary>
    public string? RepositoryType { get; set; }

    /// <summary>
    /// The package's tags.
    /// </summary>
    public string[]? Tags { get; set; }

    /// <summary>
    /// Used for optimistic concurrency.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// The package's dependencies. Each dependency group without dependencies is one item with only a target framework.
    /// </summary>
    public List<PackageDependency> Dependencies { get; set; } = [];

    /// <summary>
    /// The package's types.
    /// </summary>
    public List<PackageType> PackageTypes { get; set; } = [];

    /// <summary>
    /// The frameworks the package targets.
    /// </summary>
    public List<TargetFramework> TargetFrameworks { get; set; } = [];

    /// <summary>
    /// The lowercase normalized version, which the version is looked up by.
    /// </summary>
    public string NormalizedVersionString { get; set; } = null!;

    /// <summary>
    /// The version as the package wrote it.
    /// </summary>
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
    /// The lowercase hex SHA-256 fingerprint of the certificate that made the package's repository signature, this
    /// server's or another repository's, if it's known. When this server signed it, and it isn't the current signing
    /// certificate, the package is re-signed when it's downloaded.
    /// </summary>
    public string? RepositorySignatureFingerprint { get; set; }

    /// <summary>
    /// When the package's repository signature stops being valid, in UTC: when its certificate expires. Null if it's
    /// timestamped, or isn't known. A package mirrored from another repository whose signature expired is downloaded
    /// from that repository again, in case it was re-signed.
    /// </summary>
    public DateTime? RepositorySignatureExpiresAt { get; set; }

    /// <summary>
    /// Why the package is deprecated: "Legacy", "CriticalBugs" or "Other".
    /// Empty or null if the package isn't deprecated.
    /// </summary>
    public string[]? DeprecationReasons { get; set; }

    /// <summary>
    /// A message for users of the deprecated version.
    /// </summary>
    public string? DeprecationMessage { get; set; }

    /// <summary>
    /// A package to use instead of the deprecated version.
    /// </summary>
    public string? AlternatePackageId { get; set; }

    /// <summary>
    /// The alternate package's versions. Null for any version.
    /// </summary>
    public string? AlternatePackageVersionRange { get; set; }

    /// <summary>
    /// Whether the version is deprecated.
    /// </summary>
    public bool IsDeprecated => DeprecationReasons?.Length > 0;

    /// <summary>
    /// <see cref="IconUrl"/>, or an empty string.
    /// </summary>
    public string IconUrlString => IconUrl?.AbsoluteUri ?? string.Empty;

    /// <summary>
    /// <see cref="LicenseUrl"/>, or an empty string.
    /// </summary>
    public string LicenseUrlString => LicenseUrl?.AbsoluteUri ?? string.Empty;

    /// <summary>
    /// <see cref="ProjectUrl"/>, or an empty string.
    /// </summary>
    public string ProjectUrlString => ProjectUrl?.AbsoluteUri ?? string.Empty;

    /// <summary>
    /// <see cref="RepositoryUrl"/>, or an empty string.
    /// </summary>
    public string RepositoryUrlString => RepositoryUrl?.AbsoluteUri ?? string.Empty;
}
