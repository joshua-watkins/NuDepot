using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;

namespace NuDepot.Feed.Authentication;

/// <summary>
/// Decides what a request may do on the feed, from its API key or signed-in user.
/// </summary>
public class FeedAccess(IOptionsMonitor<FeedOptions> options)
{
    private const string LegacyApiKeyName = "ApiKey";

    private readonly IOptionsMonitor<FeedOptions> _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Whether anyone may do anything: the feed has no API keys, and users aren't enabled.
    /// </summary>
    public bool IsOpen => !HasApiKeys(_options.CurrentValue) && !UsersEnabled;

    /// <summary>
    /// Whether people can sign in with passkeys, and create their own API keys.
    /// </summary>
    public bool UsersEnabled => _options.CurrentValue.Authorization.Users.Enabled;

    /// <summary>
    /// The role of requests without a valid API key or a signed-in user. Only used when the feed isn't open.
    /// </summary>
    public FeedRole AnonymousRole => _options.CurrentValue.Authorization.AnonymousRole ?? FeedRole.ReadOnly;

    /// <summary>
    /// What the user may do. An API key or a signed-in user adds their role to what requests without
    /// one may do. In read-only mode, nobody may change the feed.
    /// </summary>
    public FeedPermissions GetPermissions(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var permissions = IsOpen ? FeedPermissions.All : AnonymousRole.GetPermissions();
        foreach (var claim in user.FindAll(ClaimTypes.Role))
        {
            if (claim.Issuer == ApiKeyAuthenticationHandler.Issuer &&
                Enum.TryParse<FeedRole>(claim.Value, out var role))
            {
                permissions |= role.GetPermissions();
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
        foreach (var (name, apiKey, role) in GetApiKeys(_options.CurrentValue))
        {
            if (CryptographicOperations.FixedTimeEquals(presented, Encoding.UTF8.GetBytes(apiKey)))
            {
                match ??= (name, role);
            }
        }

        return match;
    }

    /// <summary>
    /// Whether any API keys are configured, in <see cref="FeedOptions.ApiKey"/> or <see cref="FeedAuthorizationOptions.ApiKeys"/>.
    /// </summary>
    internal static bool HasApiKeys(FeedOptions options)
        => !string.IsNullOrEmpty(options.ApiKey) || options.Authorization.ApiKeys.Exists(apiKey => !string.IsNullOrEmpty(apiKey.Key));

    /// <summary>
    /// The configured API keys: <see cref="FeedOptions.ApiKey"/> and <see cref="FeedAuthorizationOptions.ApiKeys"/>.
    /// </summary>
    internal static List<(string Name, string Key, FeedRole Role)> GetApiKeys(FeedOptions options)
    {
        var authorization = options.Authorization;
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

            // Keys without a role don't pass validation, so the feed doesn't start with one.
            var name = string.IsNullOrWhiteSpace(apiKey.Name) ? $"{nameof(FeedAuthorizationOptions.ApiKeys)}:{i}" : apiKey.Name;
            keys.Add((name, apiKey.Key, apiKey.Role ?? FeedRole.Admin));
        }

        return keys;
    }
}
