using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NuDepot.Core;
using NuDepot.Feed.Configuration;

namespace NuDepot.Feed.Authentication;

/// <summary>
/// How the feed checks requests. The host chooses by adding the feed's authentication and authorization.
/// </summary>
public enum FeedAccessMode
{
    /// <summary>
    /// The host adds neither: anyone may do anything.
    /// </summary>
    Open,

    /// <summary>
    /// The host adds the feed's API key authentication (<c>AddAuthentication().AddNuDepotApiKey()</c>):
    /// any valid API key may do anything. Requests without one may do what
    /// <see cref="FeedAuthorizationOptions.AnonymousRole"/> allows.
    /// </summary>
    ApiKey,

    /// <summary>
    /// The host adds the feed's authorization policies too (<c>AddAuthorizationBuilder().AddNuDepotPolicies()</c>):
    /// each API key may do what its role allows.
    /// </summary>
    Roles,
}

/// <summary>
/// Decides what a request may do on the feed, from its API key or signed-in user.
/// </summary>
public class FeedAccess(
    IOptionsMonitor<NuDepotOptions> options,
    IOptionsMonitor<FeedAuthorizationOptions> authorization,
    IOptions<AuthenticationOptions> authenticationSchemes,
    IOptions<AuthorizationOptions> authorizationPolicies)
{
    private const string LegacyApiKeyName = "ApiKey";

    private readonly IOptionsMonitor<NuDepotOptions> _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IOptionsMonitor<FeedAuthorizationOptions> _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));

    /// <summary>
    /// How the feed checks requests, from what the host added.
    /// </summary>
    public FeedAccessMode Mode { get; } = GetMode(authenticationSchemes.Value, authorizationPolicies.Value);

    /// <summary>
    /// Whether the host added the feed's users, who sign in with passkeys.
    /// </summary>
    public bool UsersEnabled { get; } = HasUsers(authenticationSchemes.Value);

    /// <summary>
    /// The role of requests without a valid API key or a signed-in user. Only used when the feed isn't open.
    /// </summary>
    public FeedRole AnonymousRole => _authorization.CurrentValue.AnonymousRole ?? FeedRole.ReadOnly;

    /// <summary>
    /// What the user may do, which depends on the <see cref="Mode"/>. An API key adds to what
    /// requests without a key may do. In read-only mode, nobody may change the feed.
    /// </summary>
    public FeedPermissions GetPermissions(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var permissions = Mode == FeedAccessMode.Open ? FeedPermissions.All : AnonymousRole.GetPermissions();
        foreach (var claim in user.FindAll(ClaimTypes.Role))
        {
            if (claim.Issuer == ApiKeyAuthenticationHandler.Issuer &&
                Enum.TryParse<FeedRole>(claim.Value, out var role))
            {
                // Without the feed's authorization, a valid key is all that's needed.
                permissions |= Mode == FeedAccessMode.Roles ? role.GetPermissions() : FeedPermissions.All;
            }
        }

        if (_options.CurrentValue.IsReadOnlyMode)
        {
            permissions &= FeedPermissions.Read;
        }

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
        => _options.CurrentValue.PackageDeletionBehavior == PackageDeletionBehavior.HardDelete
            ? FeedPermissions.HardDelete
            : FeedPermissions.Unlist;

    /// <summary>
    /// Find the configured API key that matches <paramref name="key"/>.
    /// </summary>
    /// <returns>The key's name and role, or null if no key matches.</returns>
    public (string Name, FeedRole Role)? FindApiKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        var presented = Encoding.UTF8.GetBytes(key);
        (string Name, FeedRole Role)? match = null;

        // Compare against every key in constant time, so the response time doesn't reveal a key.
        foreach (var (name, apiKey, role) in GetApiKeys(_options.CurrentValue, _authorization.CurrentValue))
        {
            if (CryptographicOperations.FixedTimeEquals(presented, Encoding.UTF8.GetBytes(apiKey)))
            {
                match ??= (name, role);
            }
        }

        return match;
    }

    /// <summary>
    /// The mode for the authentication schemes and authorization policies the host added.
    /// </summary>
    internal static FeedAccessMode GetMode(AuthenticationOptions authentication, AuthorizationOptions authorization)
    {
        if (authorization.GetPolicy(FeedPolicies.Read) != null) return FeedAccessMode.Roles;

        return authentication.SchemeMap.ContainsKey(NuDepotAuthenticationSchemes.ApiKey)
            ? FeedAccessMode.ApiKey
            : FeedAccessMode.Open;
    }

    /// <summary>
    /// Whether the host added the feed's users.
    /// </summary>
    internal static bool HasUsers(AuthenticationOptions authentication)
        => authentication.SchemeMap.ContainsKey(NuDepotAuthenticationSchemes.User);

    /// <summary>
    /// The configured API keys: <see cref="NuDepotOptions.ApiKey"/> and <see cref="FeedAuthorizationOptions.ApiKeys"/>.
    /// </summary>
    internal static List<(string Name, string Key, FeedRole Role)> GetApiKeys(NuDepotOptions options, FeedAuthorizationOptions authorization)
    {
        var keys = new List<(string Name, string Key, FeedRole Role)>();

        // The original single API key may do everything, as it always could.
        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            keys.Add((LegacyApiKeyName, options.ApiKey, FeedRole.Admin));
        }

        for (var i = 0; i < authorization.ApiKeys.Count; i++)
        {
            var apiKey = authorization.ApiKeys[i];
            if (string.IsNullOrEmpty(apiKey.Key)) continue;

            // A key only needs a role with the feed's authorization, which doesn't start without one.
            var name = string.IsNullOrWhiteSpace(apiKey.Name) ? $"{nameof(FeedAuthorizationOptions.ApiKeys)}:{i}" : apiKey.Name;
            keys.Add((name, apiKey.Key, apiKey.Role ?? FeedRole.Admin));
        }

        return keys;
    }
}
