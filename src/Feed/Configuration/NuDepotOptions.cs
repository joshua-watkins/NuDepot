namespace NuDepot.Feed.Configuration;

public class NuDepotOptions
{
    /// <summary>
    /// An API key that may do everything, like an <see cref="Authentication.FeedRole.Admin"/> key in
    /// <see cref="Authorization"/>. If no API keys are set, anyone may do anything.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The API keys and their roles, and what requests without a key may do.
    /// </summary>
    public FeedAuthorizationOptions? Authorization { get; set; }

    /// <summary>
    /// The application root URL for usage in reverse proxy scenarios.
    /// </summary>
    public string? PathBase { get; set; }

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
    /// If true, nobody may change the feed, whatever their API key's role.
    /// </summary>
    public bool IsReadOnlyMode { get; set; } = false;

    /// <summary>
    /// The URLs the NuDepot server will use.
    /// As per documentation <a href="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host?view=aspnetcore-3.1#server-urls">here (Server URLs)</a>.
    /// </summary>
    public string? Urls { get; set; }

    /// <summary>
    /// The web page where users can see a package's details, with {id} and {version}
    /// placeholders, for example "packages/{id}/{version}" when the website is hosted with
    /// the server. A relative template is resolved against the server's address. If empty,
    /// clients aren't given a link. Only advertised over HTTPS, as the protocol requires.
    /// See: https://learn.microsoft.com/en-us/nuget/api/package-details-template-resource
    /// </summary>
    public string? PackageDetailsUriTemplate { get; set; }

    /// <summary>
    /// The certificate used to repository sign pushed packages. Signing is off if no certificate is set.
    /// </summary>
    public RepositorySigningOptions? RepositorySigning { get; set; }

    public DatabaseOptions? Database { get; set; }

    public StorageOptions? Storage { get; set; }

    public SearchOptions? Search { get; set; }

    public MirrorOptions? Mirror { get; set; }
}
