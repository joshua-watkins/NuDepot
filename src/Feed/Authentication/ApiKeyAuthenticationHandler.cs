using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Core;
using NuGet.Feed.Extensions;
using NuGet.Feed.Users;

namespace NuGet.Feed.Authentication;

/// <summary>
/// Authenticates requests by their API key. NuGet clients send a key in the X-NuGet-ApiKey header
/// when pushing or deleting (the push command's -k option, or nuget.config's apikeys), and send a
/// package source's credentials from nuget.config with HTTP Basic authentication. Either works:
/// the key is the header's value, or the Basic password. The Basic user name isn't used.
/// The key is a configured key, or one a user created if the application adds the feed's users.
/// </summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    FeedAccess access) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = NuGetFeedAuthenticationSchemes.ApiKey;

    /// <summary>
    /// The issuer of the claims the feed trusts for roles.
    /// </summary>
    public const string Issuer = "NuGetFeed";

    private readonly FeedAccess _access = access ?? throw new ArgumentNullException(nameof(access));

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presentedKeys = GetPresentedKeys(Request);
        if (presentedKeys.Count == 0)
        {
            return AuthenticateResult.NoResult();
        }

        foreach (var presentedKey in presentedKeys)
        {
            if (_access.FindApiKey(presentedKey) is { } apiKey)
            {
                return Success(FeedClaims.CreateIdentity(apiKey.Name, apiKey.Role, Scheme.Name));
            }

            if (_access.UsersEnabled &&
                await Context.RequestServices.GetRequiredService<UserService>().FindByApiKeyAsync(presentedKey, Context.RequestAborted) is { } user)
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
        Response.Headers.WWWAuthenticate = "Basic realm=\"NuGetFeed\", charset=\"UTF-8\"";

        return Task.CompletedTask;
    }

    private static List<string> GetPresentedKeys(HttpRequest request)
    {
        var keys = new List<string>();

        var headerKey = request.GetApiKey();
        if (!string.IsNullOrEmpty(headerKey))
        {
            keys.Add(headerKey);
        }

        if (AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var authorization) &&
            string.Equals(authorization.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) &&
            TryGetBasicPassword(authorization.Parameter, out var password))
        {
            keys.Add(password);
        }

        return keys;
    }

    private static bool TryGetBasicPassword(string? parameter, out string password)
    {
        password = string.Empty;
        if (string.IsNullOrEmpty(parameter)) return false;

        string credentials;
        try
        {
            credentials = Encoding.UTF8.GetString(Convert.FromBase64String(parameter));
        }
        catch (FormatException)
        {
            return false;
        }

        // The credentials are "user:password". Passwords may contain colons, user names may not.
        var separator = credentials.IndexOf(':');
        if (separator < 0) return false;

        password = credentials[(separator + 1)..];
        return password.Length > 0;
    }
}
