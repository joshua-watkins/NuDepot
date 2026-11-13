namespace NuDepot.Feed.Configuration;

/// <summary>
/// The feed's options, bound from the <see cref="Section"/> configuration section. The optional features' options,
/// <see cref="UserOptions"/>, <see cref="RepositorySigningOptions"/> and <see cref="MirrorOptions"/>, are in
/// subsections of it, but are only bound when the application adds those features.
/// </summary>
public class FeedOptions
{
    /// <summary>
    /// The configuration section the feed's options are bound from.
    /// </summary>
    public const string Section = "Feed";

    /// <summary>
    /// The API key for each role, which decide what anonymous requests may do.
    /// </summary>
    public FeedAuthorizationOptions Authorization { get; set; } = new();

    /// <summary>
    /// The application root URL for usage in reverse proxy scenarios.
    /// </summary>
    public string? PathBase { get; set; }

    /// <summary>
    /// Whether requests' X-Forwarded-Proto and X-Forwarded-For headers, which a reverse proxy sets, give their
    /// scheme and client address. On by default, for a reverse proxy that serves HTTPS. They're only trusted from
    /// the <see cref="TrustedProxies"/>.
    /// </summary>
    public bool UseForwardedHeaders { get; set; } = true;

    /// <summary>
    /// The addresses, like "203.0.113.7", and networks, like "203.0.113.0/24", of the reverse proxies whose
    /// forwarded headers are trusted. If empty, proxies on this machine or a private network are trusted: loopback,
    /// 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, fc00::/7 and fe80::/10.
    /// </summary>
    public List<string> TrustedProxies { get; set; } = [];

    /// <summary>
    /// If enabled, the database will be updated at app startup by running
    /// Entity Framework migrations. This is not recommended in production.
    /// </summary>
    public bool RunMigrationsAtStartup { get; set; } = true;

    /// <summary>
    /// How NuDepot should interpret package deletion requests.
    /// </summary>
    public PackageDeletionBehavior PackageDeletionBehavior { get; set; } = PackageDeletionBehavior.Unlist;

    /// <summary>
    /// If enabled, pushing a package that already exists will replace the
    /// existing package.
    /// </summary>
    public bool AllowPackageOverwrites { get; set; } = false;

    /// <summary>
    /// The default <see cref="MaxPackageSize"/>, 250 MB, which is nuget.org's limit.
    /// </summary>
    public const long DefaultMaxPackageSize = 250L * 1024 * 1024;

    /// <summary>
    /// The largest package or symbol package that may be pushed, in bytes. Larger pushes are refused with
    /// 413 Payload Too Large. 0 for no limit. A reverse proxy in front of the feed has its own limit, which needs to be
    /// at least as large.
    /// </summary>
    public long MaxPackageSize { get; set; } = DefaultMaxPackageSize;

    /// <summary>
    /// If true, nobody may change the feed, whatever their API key's role.
    /// </summary>
    public bool IsReadOnlyMode { get; set; } = false;

    /// <summary>
    /// The web page where users can see a package's details, with {id} and {version}
    /// placeholders, for example "packages/{id}/{version}" when the gallery is hosted with
    /// the feed. A relative template is resolved against the feed's address. If empty,
    /// clients aren't given a link. Only advertised over HTTPS, as the protocol requires.
    /// See: https://learn.microsoft.com/en-us/nuget/api/package-details-template-resource
    /// </summary>
    public string? PackageDetailsUriTemplate { get; set; }

    /// <summary>
    /// The package database.
    /// </summary>
    public DatabaseOptions Database { get; set; } = new();

    /// <summary>
    /// Where package and symbol files are stored.
    /// </summary>
    public StorageOptions Storage { get; set; } = new();

    /// <summary>
    /// How packages are searched.
    /// </summary>
    public SearchOptions Search { get; set; } = new();
}
