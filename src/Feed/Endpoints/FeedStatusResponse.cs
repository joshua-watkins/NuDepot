namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The feed's settings, which are in its settings files, and the state of its mirrors.
/// </summary>
internal class FeedStatusResponse
{
    /// <summary>
    /// The feed's health: each of its health checks, which "health" sums up for monitors.
    /// </summary>
    public HealthResponse Health { get; set; } = null!;

    /// <summary>
    /// The application's settings file, like the service's NuDepot.json, or an application's appsettings.json.
    /// </summary>
    public string SettingsFile { get; set; } = null!;

    /// <summary>
    /// Whether nobody may change the feed's packages.
    /// </summary>
    public bool IsReadOnlyMode { get; set; }

    /// <summary>
    /// What deleting a package does: "Unlist" or "HardDelete".
    /// </summary>
    public string PackageDeletionBehavior { get; set; } = null!;

    /// <summary>
    /// Whether pushing a package that exists replaces it.
    /// </summary>
    public bool AllowPackageOverwrites { get; set; }

    /// <summary>
    /// What requests without an API key or a signed-in user may do.
    /// </summary>
    public string AnonymousRole { get; set; } = null!;

    /// <summary>
    /// The roles whose API key is set in the settings.
    /// </summary>
    public string[] ApiKeyRoles { get; set; } = [];

    /// <summary>
    /// Whether anyone may create an account.
    /// </summary>
    public bool AllowSignUp { get; set; }

    /// <summary>
    /// The role of new accounts.
    /// </summary>
    public string DefaultRole { get; set; } = null!;

    /// <summary>
    /// Whether requests without a key or a signed-in user may read the feed.
    /// </summary>
    public bool AnonymousReads { get; set; }

    /// <summary>
    /// Whether the feed signs pushed packages.
    /// </summary>
    public bool IsSigning { get; set; }

    /// <summary>
    /// Whether the feed mirrors other feeds.
    /// </summary>
    public bool IsMirroring { get; set; }

    /// <summary>
    /// How long a mirror that couldn't be reached isn't asked, in minutes.
    /// </summary>
    public int UnreachableMirrorMinutes { get; set; }

    /// <summary>
    /// The mirrors, in the order they're asked.
    /// </summary>
    public MirrorResponse[] Mirrors { get; set; } = [];

    /// <summary>
    /// Where packages are stored.
    /// </summary>
    public string? StoragePath { get; set; }
}
