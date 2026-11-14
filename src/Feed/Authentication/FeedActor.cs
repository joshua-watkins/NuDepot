using Microsoft.AspNetCore.Http;
using NuDepot.Core;
using System.Security.Claims;

namespace NuDepot.Feed.Authentication;

/// <summary>
/// Says who made a request, for the logs: a user, one of the feed's API keys, or an anonymous client, with the
/// client's address.
/// </summary>
internal static class FeedActor
{
    /// <summary>
    /// Who made the request, like "alice (203.0.113.7)", "alice's API key (203.0.113.7)", "The ReadWrite API key
    /// (203.0.113.7)" or "An anonymous client (203.0.113.7)", which starts the messages it's in. Outside a request,
    /// like a scheduled task, it's NuDepot.
    /// </summary>
    /// <param name="context">The request, or null outside one.</param>
    /// <returns>Who made the request.</returns>
    public static string Describe(HttpContext? context)
    {
        if (context == null)
            return "NuDepot";

        return $"{Name(context.User)} ({Address(context)})";
    }

    /// <summary>
    /// The client's address, which is the one the reverse proxy forwarded if its headers are trusted.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <returns>The address, or "an unknown address".</returns>
    public static string Address(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "an unknown address";

    private static string Name(ClaimsPrincipal principal)
    {
        // Only the feed's own claims say who it is.
        Claim? name = principal.FindFirst(claim => claim.Type == ClaimTypes.Name && claim.Issuer == ApiKeyAuthenticationHandler.Issuer);
        if (name == null)
            return "An anonymous client";
        if (FeedClaims.GetUserKey(principal) == null)
            return $"The {name.Value} API key";

        return name.Subject?.AuthenticationType == NuDepotAuthenticationSchemes.ApiKey
            ? $"{name.Value}'s API key"
            : name.Value;
    }
}
