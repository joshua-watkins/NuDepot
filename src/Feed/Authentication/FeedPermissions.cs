namespace NuDepot.Feed.Authentication;

/// <summary>
/// The operations a request may perform on the feed. A <see cref="FeedRole"/> grants a set of them.
/// </summary>
[Flags]
public enum FeedPermissions
{
    /// <summary>
    /// No permissions.
    /// </summary>
    None = 0,

    /// <summary>
    /// Read the feed: the service index, search, package metadata and content, symbols,
    /// the catalog and known vulnerabilities.
    /// </summary>
    Read = 1 << 0,

    /// <summary>
    /// Push packages and symbol packages.
    /// </summary>
    Push = 1 << 1,

    /// <summary>
    /// Unlist and relist packages. Deleting a package unlists it when
    /// <see cref="Configuration.FeedOptions.PackageDeletionBehavior"/> is <see cref="Configuration.PackageDeletionBehavior.Unlist"/>.
    /// </summary>
    Unlist = 1 << 2,

    /// <summary>
    /// Deprecate and undeprecate packages.
    /// </summary>
    Deprecate = 1 << 3,

    /// <summary>
    /// Permanently remove packages: deleting a package when <see cref="Configuration.FeedOptions.PackageDeletionBehavior"/>
    /// is <see cref="Configuration.PackageDeletionBehavior.HardDelete"/>, and pushing a package that replaces an existing one
    /// when <see cref="Configuration.FeedOptions.AllowPackageOverwrites"/> is enabled.
    /// </summary>
    HardDelete = 1 << 4,

    /// <summary>
    /// Record and remove the feed's known vulnerabilities.
    /// </summary>
    ManageVulnerabilities = 1 << 5,

    /// <summary>
    /// Administer the feed: manage its users, rotate its generated signing certificate and see its settings.
    /// It doesn't change packages, so read-only mode doesn't take it away.
    /// </summary>
    Administer = 1 << 6,

    /// <summary>
    /// Every permission.
    /// </summary>
    All = Read | Push | Unlist | Deprecate | HardDelete | ManageVulnerabilities | Administer,
}
