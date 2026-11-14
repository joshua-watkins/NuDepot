using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NuDepot.Feed.Authentication;
using System.Globalization;
using System.Security.Claims;

namespace NuDepot.Feed.Users;

/// <summary>
/// Checks the sign-in cookie's session in the database with each request, and answers with status
/// codes instead of redirecting to a sign-in page, since NuGet clients use the same endpoints.
/// </summary>
internal sealed partial class UserCookieEvents : CookieAuthenticationEvents
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
        string? value = properties.GetString(SessionItem);
        int separator = value?.IndexOf(':') ?? -1;
        if (separator < 0)
            return null;

        return int.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out int key)
            ? (key, value![(separator + 1)..])
            : null;
    }

    /// <summary>
    /// The session's user is loaded with each request, since their name and role can change, and
    /// their account can be deleted. Every few minutes, a request also replaces the cookie's token, and
    /// the cookie lasts another year, so a user who keeps using the feed stays signed in.
    /// </summary>
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        // The browser sends the cookie with requests that other sites on the same domain, like another port or
        // subdomain, make it send. They can't change anything: those requests continue without the user.
        HttpRequest request = context.HttpContext.Request;
        if (IsFromAnotherOrigin(request))
        {
            Log.CrossSiteRequest(
                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger<UserCookieEvents>(),
                request.Method,
                request.Path,
                FeedActor.Address(context.HttpContext),
                request.Headers.Origin.ToString() is { Length: > 0 } origin ? origin : "an unknown origin");
            context.RejectPrincipal();
            return;
        }

        (int Key, string Token)? session = GetSession(context.Properties);
        SessionValidation result = session == null
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
            DateTimeOffset now = (context.Options.TimeProvider ?? TimeProvider.System).GetUtcNow();
            SetSession(context.Properties, session!.Value.Key, result.NewToken);
            context.Properties.IssuedUtc = now;
            context.Properties.ExpiresUtc = now + UserService.SessionLifetime;
            context.ShouldRenew = true;
        }
    }

    /// <summary>
    /// Whether a request that may change something was made by a page from another origin. Browsers say where
    /// requests come from in Sec-Fetch-Site, or at least in Origin. Requests without either aren't from browsers.
    /// </summary>
    internal static bool IsFromAnotherOrigin(HttpRequest request)
    {
        string method = request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
            return false;

        string? site = request.Headers["Sec-Fetch-Site"];
        if (!string.IsNullOrEmpty(site))
            return site is not ("same-origin" or "none");

        string? origin = request.Headers.Origin;
        if (string.IsNullOrEmpty(origin))
            return false;

        string expected = request.Scheme + "://" + request.Host.ToUriComponent();
        return !string.Equals(origin, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored the sign-in cookie of {Method} {Path} from {Address}, because a page from another site, {Origin}, sent it")]
        public static partial void CrossSiteRequest(ILogger logger, string method, PathString path, string address, string origin);
    }
}
