using NuDepot.Feed.Entities;
using System.Globalization;
using System.Security.Claims;

namespace NuDepot.Feed.Authentication;

/// <summary>
/// The claims the feed issues for API keys and users. <see cref="FeedAccess"/> only trusts roles the feed issued.
/// </summary>
public static class FeedClaims
{
    /// <summary>
    /// The identity of a configured API key.
    /// </summary>
    public static ClaimsIdentity CreateIdentity(string name, FeedRole role, string authenticationType)
    {
        return new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, name, ClaimValueTypes.String, ApiKeyAuthenticationHandler.Issuer),
                new Claim(ClaimTypes.Role, role.ToString(), ClaimValueTypes.String, ApiKeyAuthenticationHandler.Issuer),
            ],
            authenticationType);
    }

    /// <summary>
    /// The identity of a user, signed in or using one of their API keys.
    /// </summary>
    public static ClaimsIdentity CreateIdentity(User user, string authenticationType)
    {
        ClaimsIdentity identity = CreateIdentity(user.UserName, user.Role, authenticationType);
        identity.AddClaim(new Claim(
            ClaimTypes.NameIdentifier,
            user.Key.ToString(CultureInfo.InvariantCulture),
            ClaimValueTypes.Integer32,
            ApiKeyAuthenticationHandler.Issuer));

        return identity;
    }

    /// <summary>
    /// The key of the signed-in user, or null if the principal isn't a user.
    /// </summary>
    public static int? GetUserKey(ClaimsPrincipal principal)
    {
        Claim? claim = principal.FindFirst(ClaimTypes.NameIdentifier);
        if (claim?.Issuer != ApiKeyAuthenticationHandler.Issuer)
            return null;

        return int.TryParse(claim.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int key) ? key : null;
    }
}
