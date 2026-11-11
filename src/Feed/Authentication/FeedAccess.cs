using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace NuDepot.Feed.Authentication;

/// <summary>
/// Decides what a request may do on the feed, from its API key or signed-in user.
/// </summary>
public class FeedAccess(IOptionsMonitor<FeedOptions> options, IOptionsMonitor<UserOptions> users)
{
    private IOptionsMonitor<FeedOptions> Options { get; } = options;
    private IOptionsMonitor<UserOptions> Users { get; } = users;

    /// <summary>
    /// Whether people can sign in with passkeys, and create API keys for NuGet clients: the application added
    /// users, with <c>AddUsers()</c> or <c>EnableUsers()</c>, and they're on.
    /// </summary>
    public bool UsersEnabled => Users.CurrentValue.IsEnabled;

    /// <summary>
    /// The role of requests without a valid API key or a signed-in user: <see cref="FeedRole.ReadWrite"/>, unless
    /// users or a <see cref="ApiKeyOptions.ReadWrite"/> key make it <see cref="FeedRole.ReadOnly"/>, or a
    /// <see cref="ApiKeyOptions.ReadOnly"/> key, or users without <see cref="UserOptions.AnonymousReads"/>, make
    /// it <see cref="FeedRole.None"/>.
    /// </summary>
    public FeedRole AnonymousRole => GetAnonymousRole(Options.CurrentValue.Authorization.ApiKeys, Users.CurrentValue);

    /// <summary>
    /// What the user may do. An API key or a signed-in user adds their role to what requests without
    /// one may do. In read-only mode, nobody may change the feed's packages.
    /// </summary>
    public FeedPermissions GetPermissions(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        FeedPermissions permissions = AnonymousRole.GetPermissions();
        foreach (Claim claim in user.FindAll(ClaimTypes.Role))
        {
            if (claim.Issuer == ApiKeyAuthenticationHandler.Issuer &&
                Enum.TryParse<FeedRole>(claim.Value, out FeedRole role))
                permissions |= role.GetPermissions();
        }

        if (Options.CurrentValue.IsReadOnlyMode)
            permissions &= FeedPermissions.Read | FeedPermissions.Administer;

        return permissions;
    }

    /// <summary>
    /// Whether the user may do all of <paramref name="permissions"/>.
    /// </summary>
    public bool HasPermission(ClaimsPrincipal user, FeedPermissions permissions)
        => (GetPermissions(user) & permissions) == permissions;

    /// <summary>
    /// The permission needed to delete a package, which depends on whether deleting unlists it.
    /// </summary>
    public FeedPermissions PackageDeletionPermission
        => Options.CurrentValue.PackageDeletionBehavior == PackageDeletionBehavior.HardDelete
            ? FeedPermissions.HardDelete
            : FeedPermissions.Unlist;

    /// <summary>
    /// Find the role whose configured API key is <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The presented key.</param>
    /// <param name="role">The role named by the credentials' user name, or null for a key in the X-NuGet-ApiKey header.</param>
    /// <returns>The key's role, or null if it isn't a configured key, or isn't the named role's.</returns>
    public FeedRole? FindApiKey(string? key, FeedRole? role = null)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        byte[] presented = Encoding.UTF8.GetBytes(key);
        FeedRole? match = null;

        // Compare against every key in constant time, so the response time doesn't reveal a key.
        foreach ((FeedRole keyRole, string apiKey) in GetApiKeys(Options.CurrentValue.Authorization.ApiKeys))
        {
            if (CryptographicOperations.FixedTimeEquals(presented, Encoding.UTF8.GetBytes(apiKey)) && (role == null || role == keyRole))
                match ??= keyRole;
        }

        return match;
    }

    /// <summary>
    /// The role a user name names, in a package source's credentials, like "ReadWrite".
    /// </summary>
    public static FeedRole? GetRole(string userName)
    {
        foreach (FeedRole role in ApiKeyRoles)
        {
            if (string.Equals(userName, role.ToString(), StringComparison.OrdinalIgnoreCase))
                return role;
        }

        return null;
    }

    /// <summary>
    /// The roles that have a configured API key, and that users can't be named after.
    /// </summary>
    internal static IReadOnlyList<FeedRole> ApiKeyRoles { get; } = [FeedRole.ReadOnly, FeedRole.ReadWrite, FeedRole.Admin];

    /// <summary>
    /// The configured API keys, with their roles.
    /// </summary>
    internal static List<(FeedRole Role, string Key)> GetApiKeys(ApiKeyOptions apiKeys)
    {
        List<(FeedRole Role, string Key)> keys = [];
        if (!string.IsNullOrEmpty(apiKeys.ReadOnly))
            keys.Add((FeedRole.ReadOnly, apiKeys.ReadOnly));
        if (!string.IsNullOrEmpty(apiKeys.ReadWrite))
            keys.Add((FeedRole.ReadWrite, apiKeys.ReadWrite));
        if (!string.IsNullOrEmpty(apiKeys.Admin))
            keys.Add((FeedRole.Admin, apiKeys.Admin));

        return keys;
    }

    internal static FeedRole GetAnonymousRole(ApiKeyOptions apiKeys, UserOptions users)
    {
        bool usersEnabled = users.IsEnabled;
        if (!string.IsNullOrEmpty(apiKeys.ReadOnly) || (usersEnabled && !users.AnonymousReads))
            return FeedRole.None;

        // Users change the feed, so others may only read it.
        if (!string.IsNullOrEmpty(apiKeys.ReadWrite) || usersEnabled)
            return FeedRole.ReadOnly;

        return FeedRole.ReadWrite;
    }
}
