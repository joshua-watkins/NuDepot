using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Core;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Users;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;

namespace NuDepot.Feed.Authentication;

/// <summary>
/// Authenticates requests by their API key. NuGet clients send a key in the X-NuGet-ApiKey header when pushing or
/// deleting (the push command's -k option, or nuget.config's apikeys), and send a package source's credentials from
/// nuget.config with HTTP Basic authentication, whenever the feed asks for them.
/// <list type="bullet">
/// <item>The feed's configured keys work either way. In credentials, the user name is the key's role, like "ReadWrite".</item>
/// <item>Keys users create only work in credentials, with the user's name, and only if the application uses <c>MapUsers()</c>.</item>
/// </list>
/// </summary>
internal sealed class ApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, FeedAccess access) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = NuDepotAuthenticationSchemes.ApiKey;

    /// <summary>
    /// The issuer of the claims the feed trusts for roles.
    /// </summary>
    public const string Issuer = "NuDepot";

    private FeedAccess Access { get; } = access;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? headerKey = Request.GetApiKey();
        (string UserName, string Password)? credentials = GetCredentials(Request);
        if (string.IsNullOrEmpty(headerKey) && credentials == null)
            return AuthenticateResult.NoResult();

        if (Access.FindApiKey(headerKey) is { } headerRole)
            return Success(FeedClaims.CreateIdentity(headerRole.ToString(), headerRole, Scheme.Name));

        if (credentials is ({ } userName, { } password))
        {
            // The user name says which key it is: a role's, or a user's.
            if (FeedAccess.GetRole(userName) is { } role)
            {
                if (Access.FindApiKey(password, role) is { } keyRole)
                    return Success(FeedClaims.CreateIdentity(keyRole.ToString(), keyRole, Scheme.Name));
            }
            else if (Access.UsersEnabled &&
                await Context.RequestServices.GetRequiredService<UserService>().FindByApiKeyAsync(password, Context.RequestAborted) is { } user &&
                string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase))
            {
                return Success(FeedClaims.CreateIdentity(user, Scheme.Name));
            }
        }

        // The request continues as anonymous, so an unknown key can still do what anyone may.
        return AuthenticateResult.Fail("The API key isn't valid.");
    }

    private AuthenticateResult Success(ClaimsIdentity identity)
        => AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // Asking for Basic credentials makes NuGet clients, including Visual Studio,
        // retry with the package source's credentials from nuget.config.
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Basic realm=\"NuDepot\", charset=\"UTF-8\"";

        return Task.CompletedTask;
    }

    private static (string UserName, string Password)? GetCredentials(HttpRequest request)
    {
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out AuthenticationHeaderValue? authorization) ||
            !string.Equals(authorization.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(authorization.Parameter))
            return null;

        string credentials;
        try
        {
            credentials = Encoding.UTF8.GetString(Convert.FromBase64String(authorization.Parameter));
        }
        catch (FormatException)
        {
            return null;
        }

        // The credentials are "user:password". Passwords may contain colons, user names may not.
        int separator = credentials.IndexOf(':');
        if (separator < 0 || separator == credentials.Length - 1)
            return null;

        return (credentials[..separator], credentials[(separator + 1)..]);
    }
}
