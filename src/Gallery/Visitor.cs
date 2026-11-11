using Microsoft.Extensions.Logging;

namespace NuDepot.Gallery;

/// <summary>
/// Who's visiting the gallery, and what they may do on the feed. Only signed-in users manage the feed from the
/// gallery: without users, the feed is managed with NuGet clients and the feed's API keys. Each request has its own.
/// </summary>
/// <param name="feed">The feed the gallery shows.</param>
/// <param name="logger">The logger.</param>
public sealed class Visitor(FeedConnection feed, ILogger<Visitor> logger)
{
    private FeedConnection Feed { get; } = feed;
    private ILogger<Visitor> Logger { get; } = logger;
    private HashSet<string> Permissions { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether a user is signed in.
    /// </summary>
    public bool IsSignedIn { get; private set; }

    /// <summary>
    /// The signed-in user's role, like "ReadWrite".
    /// </summary>
    public string? Role { get; private set; }

    /// <summary>
    /// Whether deleting a package removes it permanently, rather than unlisting it.
    /// </summary>
    public bool DeletesPermanently { get; private set; }

    /// <summary>
    /// Whether the signed-in user may push packages.
    /// </summary>
    public bool CanPush => Can("Push");

    /// <summary>
    /// Whether the signed-in user may unlist and relist packages.
    /// </summary>
    public bool CanUnlist => Can("Unlist");

    /// <summary>
    /// Whether the signed-in user may delete packages: unlist them, or remove them permanently, as the feed does.
    /// </summary>
    public bool CanDelete => DeletesPermanently ? Can("HardDelete") : CanUnlist;

    /// <summary>
    /// Whether the signed-in user may deprecate packages.
    /// </summary>
    public bool CanDeprecate => Can("Deprecate");

    /// <summary>
    /// Whether the signed-in user may record and remove known vulnerabilities.
    /// </summary>
    public bool CanManageVulnerabilities => Can("ManageVulnerabilities");

    /// <summary>
    /// Whether the signed-in user administers the feed: its users, signing and settings.
    /// </summary>
    public bool CanAdminister => Can("Administer");

    /// <summary>
    /// Whether the signed-in user may manage packages at all.
    /// </summary>
    public bool CanManagePackages => CanUnlist || CanDelete || CanDeprecate || CanManageVulnerabilities;

    /// <summary>
    /// Whether the signed-in user's role may push, but the feed is in read-only mode.
    /// </summary>
    public bool IsReadOnlyMode => IsSignedIn && Role is "ReadWrite" or "Admin" && !CanPush;

    /// <summary>
    /// Asks the feed what the signed-in user may do. If the feed can't say, they may only browse.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>A task that completes when the visitor's properties are set.</returns>
    internal async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            FeedAccount? account = await Feed.GetAccountOrNullAsync(cancellationToken);
            if (account is not { SignedIn: true })
                return;

            IsSignedIn = true;
            Role = account.Role;
            DeletesPermanently = account.PackageDeletion == "HardDelete";
            Permissions.UnionWith(account.Permissions ?? []);
        }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException)
        {
            Logger.LogWarning(e, "Could not ask the feed what the signed-in user may do; they may only browse");
        }
    }

    private bool Can(string permission) => Permissions.Contains(permission);
}

/// <summary>
/// The feed's answer to who's signed in: its api/account endpoint.
/// </summary>
/// <param name="SignedIn">Whether someone is signed in.</param>
/// <param name="UserName">The signed-in user's name.</param>
/// <param name="Role">The signed-in user's role.</param>
/// <param name="Permissions">What the signed-in user may do, like "Push".</param>
/// <param name="PackageDeletion">What deleting a package does: "Unlist" or "HardDelete".</param>
internal sealed record FeedAccount(bool SignedIn, string? UserName, string? Role, string[]? Permissions, string? PackageDeletion);
