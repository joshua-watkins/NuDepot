using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Fido2NetLib.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NuDepot.Core;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Serialization;
using NuDepot.Feed.Users;

namespace NuDepot.Feed.Endpoints;

/// <summary>
/// Accounts for people who sign in with passkeys, used by the website. These aren't part of the
/// NuGet protocol. They use the sign-in cookie, never API keys.
/// </summary>
internal static class AccountEndpoints
{
    private const string DefaultPasskeyName = "Passkey";

    // GET api/account
    public static async Task<IResult> GetAsync(HttpContext httpContext, UserService users, CancellationToken cancellationToken)
    {
        var user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
        var isFirstUser = user == null && !await users.AnyAsync(cancellationToken);

        return Results.Json(
            new AccountResponse
            {
                SignedIn = user != null,
                UserName = user?.UserName,
                Role = user?.Role.ToString(),
                CanSignUp = user == null && await users.CanSignUpAsync(cancellationToken),
                IsFirstUser = isFirstUser,
            },
            AccountJsonContext.Default.AccountResponse);
    }

    // POST api/account/signup
    // Starts creating an account. The browser then creates a passkey and sends it to api/account/passkeys?userName=.
    public static async Task<IResult> SignUpAsync(
        HttpContext httpContext,
        UserService users,
        PasskeyService passkeys,
        CancellationToken cancellationToken)
    {
        var body = await EndpointHelpers.ReadJsonOrNullAsync(httpContext.Request, AccountJsonContext.Default.PasskeyCreationRequest, cancellationToken);
        var userName = body?.UserName?.Trim();

        if (!await users.CanSignUpAsync(cancellationToken))
        {
            return EndpointHelpers.Text(StatusCodes.Status403Forbidden, "This feed doesn't allow new accounts.");
        }

        if (!UserService.IsValidUserName(userName))
        {
            return EndpointHelpers.Text(
                StatusCodes.Status400BadRequest,
                "A user name is up to 64 letters, digits, '.', '_', '-' or '@', and starts with a letter or digit.");
        }

        var options = await passkeys.BeginSignUpAsync(httpContext, userName!, GetName(body?.PasskeyName, DefaultPasskeyName)!, cancellationToken);
        if (options == null)
        {
            return EndpointHelpers.Text(StatusCodes.Status409Conflict, $"The user name '{userName}' is taken.");
        }

        return Results.Json(options, FidoModelSerializerContext.Default.CredentialCreateOptions);
    }

    // POST api/account/passkeys/options
    // Starts adding a passkey to the signed-in user's account.
    public static async Task<IResult> AddPasskeyOptionsAsync(
        HttpContext httpContext,
        UserService users,
        PasskeyService passkeys,
        CancellationToken cancellationToken)
    {
        var user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
        if (user == null) return Results.Unauthorized();

        var body = await EndpointHelpers.ReadJsonOrNullAsync(httpContext.Request, AccountJsonContext.Default.PasskeyCreationRequest, cancellationToken);
        var options = await passkeys.BeginAddPasskeyAsync(httpContext, user, GetName(body?.PasskeyName, DefaultPasskeyName)!, cancellationToken);

        return Results.Json(options, FidoModelSerializerContext.Default.CredentialCreateOptions);
    }

    // POST api/account/passkeys?userName=
    // Finishes creating the account with the user name, which signs the new user in. Without a user name,
    // finishes adding a passkey to the signed-in user's account.
    public static async Task<IResult> CreatePasskeyAsync(
        string? userName,
        HttpContext httpContext,
        UserService users,
        PasskeyService passkeys,
        CancellationToken cancellationToken)
    {
        var response = await EndpointHelpers.ReadJsonOrNullAsync(
            httpContext.Request,
            AccountJsonContext.Default.AuthenticatorAttestationRawResponse,
            cancellationToken);
        if (response == null) return Results.BadRequest();

        PasskeyResult result;
        if (userName != null)
        {
            result = await passkeys.CompleteSignUpAsync(httpContext, userName, response, cancellationToken);
        }
        else
        {
            var user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
            if (user == null) return Results.Unauthorized();

            result = await passkeys.CompleteAddPasskeyAsync(httpContext, user, response, cancellationToken);
        }

        if (result.User == null)
        {
            return EndpointHelpers.Text(StatusCodes.Status400BadRequest, result.Error!);
        }

        if (userName != null)
        {
            await SignInAsync(httpContext, users, result.User, cancellationToken);
        }

        return Results.NoContent();
    }

    // POST api/account/signin/options
    public static async Task<IResult> SignInOptionsAsync(HttpContext httpContext, PasskeyService passkeys, CancellationToken cancellationToken)
    {
        var body = await EndpointHelpers.ReadJsonOrNullAsync(httpContext.Request, AccountJsonContext.Default.SignInRequest, cancellationToken);
        var userName = body?.UserName?.Trim();

        var options = string.IsNullOrEmpty(userName) ? null : await passkeys.BeginSignInAsync(httpContext, userName, cancellationToken);
        if (options == null)
        {
            return EndpointHelpers.Text(StatusCodes.Status400BadRequest, $"There's no account named '{userName}'.");
        }

        return Results.Json(options, FidoModelSerializerContext.Default.AssertionOptions);
    }

    // POST api/account/signin
    public static async Task<IResult> SignInAsync(
        HttpContext httpContext,
        UserService users,
        PasskeyService passkeys,
        CancellationToken cancellationToken)
    {
        var response = await EndpointHelpers.ReadJsonOrNullAsync(
            httpContext.Request,
            AccountJsonContext.Default.AuthenticatorAssertionRawResponse,
            cancellationToken);
        if (response == null) return Results.BadRequest();

        var result = await passkeys.CompleteSignInAsync(httpContext, response, cancellationToken);
        if (result.User == null)
        {
            return EndpointHelpers.Text(StatusCodes.Status400BadRequest, result.Error!);
        }

        await SignInAsync(httpContext, users, result.User, cancellationToken);
        return Results.NoContent();
    }

    // POST api/account/signout
    // This only takes the HttpContext, so it's mapped as a RequestDelegate, which can't return a result.
    public static async Task SignOutAsync(HttpContext httpContext)
    {
        var result = await httpContext.AuthenticateAsync(NuDepotAuthenticationSchemes.User);
        var session = result.Properties == null ? null : UserCookieEvents.GetSession(result.Properties);
        if (result.Succeeded && session != null)
        {
            await httpContext.RequestServices.GetRequiredService<UserService>()
                .DeleteSessionAsync(session.Value.Key, httpContext.RequestAborted);
        }

        await httpContext.SignOutAsync(NuDepotAuthenticationSchemes.User);
        httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    // GET api/account/passkeys
    public static async Task<IResult> GetPasskeysAsync(HttpContext httpContext, UserService users, CancellationToken cancellationToken)
    {
        var user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
        if (user == null) return Results.Unauthorized();

        var passkeys = await users.GetPasskeysAsync(user.Key, cancellationToken);

        return Results.Json(
            passkeys.Select(p => new PasskeyResponse
            {
                Id = p.Key,
                Name = p.Name,
                CreatedAt = p.CreatedAt,
                LastUsedAt = p.LastUsedAt,
                IsBackedUp = p.IsBackedUp,
            }).ToArray(),
            AccountJsonContext.Default.PasskeyResponseArray);
    }

    // DELETE api/account/passkeys/{id}
    public static async Task<IResult> DeletePasskeyAsync(int id, HttpContext httpContext, UserService users, CancellationToken cancellationToken)
    {
        var user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
        if (user == null) return Results.Unauthorized();

        return await users.DeletePasskeyAsync(user.Key, id, cancellationToken) switch
        {
            PasskeyDeletionResult.Deleted => Results.NoContent(),
            PasskeyDeletionResult.LastPasskey => EndpointHelpers.Text(
                StatusCodes.Status409Conflict,
                "You can't remove your last passkey: you need it to sign in."),
            _ => Results.NotFound(),
        };
    }

    // GET api/account/apikeys
    public static async Task<IResult> GetApiKeysAsync(HttpContext httpContext, UserService users, CancellationToken cancellationToken)
    {
        var user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
        if (user == null) return Results.Unauthorized();

        var apiKeys = await users.GetApiKeysAsync(user.Key, cancellationToken);

        return Results.Json(apiKeys.Select(k => ToResponse(k, key: null)).ToArray(), AccountJsonContext.Default.ApiKeyResponseArray);
    }

    // POST api/account/apikeys
    public static async Task<IResult> CreateApiKeyAsync(HttpContext httpContext, UserService users, CancellationToken cancellationToken)
    {
        var user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
        if (user == null) return Results.Unauthorized();

        var body = await EndpointHelpers.ReadJsonOrNullAsync(httpContext.Request, AccountJsonContext.Default.ApiKeyCreationRequest, cancellationToken);
        var name = GetName(body?.Name, defaultName: null);
        if (name == null)
        {
            return EndpointHelpers.Text(StatusCodes.Status400BadRequest, "An API key needs a name, like \"Build server\".");
        }

        var (apiKey, key) = await users.CreateApiKeyAsync(user.Key, name, cancellationToken);

        return Results.Json(ToResponse(apiKey, key), AccountJsonContext.Default.ApiKeyResponse, statusCode: StatusCodes.Status201Created);
    }

    // DELETE api/account/apikeys/{id}
    public static async Task<IResult> DeleteApiKeyAsync(int id, HttpContext httpContext, UserService users, CancellationToken cancellationToken)
    {
        var user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
        if (user == null) return Results.Unauthorized();

        return await users.DeleteApiKeyAsync(user.Key, id, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }

    /// <summary>
    /// The user signed in with the sign-in cookie. API keys don't count: they can't manage accounts.
    /// </summary>
    private static async Task<User?> GetSignedInUserAsync(HttpContext httpContext, UserService users, CancellationToken cancellationToken)
    {
        var result = await httpContext.AuthenticateAsync(NuDepotAuthenticationSchemes.User);
        var userKey = result.Succeeded ? FeedClaims.GetUserKey(result.Principal) : null;

        return userKey == null ? null : await users.FindAsync(userKey.Value, cancellationToken);
    }

    private static async Task SignInAsync(HttpContext httpContext, UserService users, User user, CancellationToken cancellationToken)
    {
        var principal = new ClaimsPrincipal(FeedClaims.CreateIdentity(user, NuDepotAuthenticationSchemes.User));
        var properties = new AuthenticationProperties { IsPersistent = true };

        var (session, token) = await users.CreateSessionAsync(user.Key, cancellationToken);
        UserCookieEvents.SetSession(properties, session.Key, token);

        await httpContext.SignInAsync(NuDepotAuthenticationSchemes.User, principal, properties);
    }

    private static string? GetName(string? name, string? defaultName)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return defaultName;

        return name.Length > NuDepotContext.MaxCredentialNameLength ? name[..NuDepotContext.MaxCredentialNameLength] : name;
    }

    private static ApiKeyResponse ToResponse(UserApiKey apiKey, string? key) => new()
    {
        Id = apiKey.Key,
        Name = apiKey.Name,
        Prefix = apiKey.Prefix,
        CreatedAt = apiKey.CreatedAt,
        LastUsedAt = apiKey.LastUsedAt,
        Key = key,
    };
}
