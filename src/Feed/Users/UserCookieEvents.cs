using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NuDepot.Feed.Authentication;

namespace NuDepot.Feed.Users;

/// <summary>
/// Checks the sign-in cookie's session in the database with each request, and answers with status
/// codes instead of redirecting to a sign-in page, since NuGet clients use the same endpoints.
/// </summary>
internal sealed class UserCookieEvents : CookieAuthenticationEvents
{
    // The cookie's session, as "{key}:{token}".
    private const string SessionItem = "NuDepot.Session";

    public UserCookieEvents()
    {
        OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };

        OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    public static void SetSession(AuthenticationProperties properties, int sessionKey, string token)
        => properties.SetString(SessionItem, sessionKey.ToString(CultureInfo.InvariantCulture) + ":" + token);

    public static (int Key, string Token)? GetSession(AuthenticationProperties properties)
    {
        var value = properties.GetString(SessionItem);
        var separator = value?.IndexOf(':') ?? -1;
        if (separator < 0) return null;

        return int.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var key)
            ? (key, value![(separator + 1)..])
            : null;
    }

    /// <summary>
    /// The session's user is loaded with each request, since their name and role can change, and
    /// their account can be deleted. Each request also replaces the cookie's token, and the cookie
    /// lasts another year, so a user who keeps using the feed stays signed in.
    /// </summary>
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var session = GetSession(context.Properties);
        var result = session == null
            ? SessionValidation.SignedOut
            : await context.HttpContext.RequestServices.GetRequiredService<UserService>()
                .ValidateSessionAsync(session.Value.Key, session.Value.Token, context.HttpContext.RequestAborted);

        if (result.User == null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(context.Scheme.Name);
            return;
        }

        context.ReplacePrincipal(new ClaimsPrincipal(FeedClaims.CreateIdentity(result.User, context.Scheme.Name)));

        if (result.NewToken != null)
        {
            // Renewing keeps the cookie's lifetime from when it was issued, so this makes it the full lifetime from now.
            var now = (context.Options.TimeProvider ?? TimeProvider.System).GetUtcNow();
            SetSession(context.Properties, session!.Value.Key, result.NewToken);
            context.Properties.IssuedUtc = now;
            context.Properties.ExpiresUtc = now + UserService.SessionLifetime;
            context.ShouldRenew = true;
        }
    }
}
