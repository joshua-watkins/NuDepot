using Fido2NetLib;
using Fido2NetLib.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NuDepot.Core;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Serialization;
using NuDepot.Feed.Users;
using System.Security.Claims;

namespace NuDepot.Feed.Endpoints;

/// <summary>
/// Accounts for people who sign in with passkeys, used by the website. These aren't part of the
/// NuGet protocol. They use the sign-in cookie, never API keys.
/// </summary>
internal static partial class AccountEndpoints
{
    private const string DefaultPasskeyName = "Passkey";
    private const string LoggerCategory = "NuDepot.Feed.Endpoints.Account";

    // GET api/account
    public static async Task<IResult> GetAsync(HttpContext httpContext, UserService users, FeedAccess access, CancellationToken cancellationToken)
    {
        AuthenticateResult result = await httpContext.AuthenticateAsync(NuDepotAuthenticationSchemes.User);
        User? user = await GetSignedInUserAsync(result, users, cancellationToken);
        bool isFirstUser = user == null && !await users.AnyAsync(cancellationToken);

        return Results.Json(
            new AccountResponse
            {
                SignedIn = user != null,
                UserName = user?.UserName,
                Role = user?.Role.ToString(),
                CanSignUp = user == null && await users.CanSignUpAsync(cancellationToken),
                IsFirstUser = isFirstUser,
                Permissions = user == null ? null : PermissionNames(access.GetPermissions(result.Principal!)),
                PackageDeletion = user == null ? null : access.PackageDeletionPermission == FeedPermissions.HardDelete ? "HardDelete" : "Unlist",
            },
            AccountJsonContext.Default.AccountResponse);
    }

    // GET api/account/sessions
    public static async Task<IResult> GetSessionsAsync(HttpContext httpContext, UserService users, CancellationToken cancellationToken)
    {
        AuthenticateResult result = await httpContext.AuthenticateAsync(NuDepotAuthenticationSchemes.User);
        User? user = await GetSignedInUserAsync(result, users, cancellationToken);
        if (user == null)
            return Results.Unauthorized();

        int? current = result.Properties == null ? null : UserCookieEvents.GetSession(result.Properties)?.Key;
        List<UserSession> sessions = await users.GetSessionsAsync(user.Key, cancellationToken);

        return Results.Json(
            [.. sessions.Select(s => new SessionResponse
            {
                Id = s.Key,
                CreatedAt = s.CreatedAt,
                LastUsedAt = s.RotatedAt,
                IsCurrent = s.Key == current,
            })],
            AccountJsonContext.Default.SessionResponseArray);
    }

    // DELETE api/account/sessions/{id}
    // Signs one of the user's browsers out. Signing this browser out is what api/account/signout does.
    public static async Task<IResult> DeleteSessionAsync(int id, HttpContext httpContext, UserService users, CancellationToken cancellationToken)
    {
        User? user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
        if (user == null)
            return Results.Unauthorized();

        return await users.DeleteSessionAsync(user.Key, id, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }

    /// <summary>
    /// The names of the permissions, like "Push".
    /// </summary>
    internal static string[] PermissionNames(FeedPermissions permissions)
    {
        return [.. Enum
            .GetValues<FeedPermissions>()
            .Where(p => p != FeedPermissions.None && p != FeedPermissions.All && permissions.HasFlag(p))
            .Select(p => p.ToString())];
    }

    // POST api/account/signup
    // Starts creating an account. The browser then creates a passkey and sends it to api/account/passkeys?userName=.
    public static async Task<IResult> SignUpAsync(HttpContext httpContext, UserService users, PasskeyService passkeys, CancellationToken cancellationToken)
    {
        PasskeyCreationRequest? body = await EndpointHelpers.ReadJsonOrNullAsync(httpContext.Request, AccountJsonContext.Default.PasskeyCreationRequest, cancellationToken);
        string? userName = body?.UserName?.Trim();

        if (!await users.CanSignUpAsync(cancellationToken))
            return EndpointHelpers.Text(StatusCodes.Status403Forbidden, "This feed doesn't allow new accounts.");

        if (!UserService.IsValidUserName(userName))
        {
            return EndpointHelpers.Text(
                StatusCodes.Status400BadRequest,
                "A user name is up to 64 letters, digits, '.', '_', '-' or '@', and starts with a letter or digit. " +
                "It can't be ReadOnly, ReadWrite or Admin.");
        }

        CredentialCreateOptions? options = await passkeys.BeginSignUpAsync(httpContext, userName!, GetName(body?.PasskeyName, DefaultPasskeyName)!, cancellationToken);
        if (options == null)
            return EndpointHelpers.Text(StatusCodes.Status409Conflict, $"The user name '{userName}' is taken.");

        return Results.Json(options, FidoModelSerializerContext.Default.CredentialCreateOptions);
    }

    // POST api/account/passkeys/options
    // Starts adding a passkey to the signed-in user's account.
    public static async Task<IResult> AddPasskeyOptionsAsync(HttpContext httpContext, UserService users, PasskeyService passkeys, CancellationToken cancellationToken)
    {
        User? user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
        if (user == null)
            return Results.Unauthorized();

        PasskeyCreationRequest? body = await EndpointHelpers.ReadJsonOrNullAsync(httpContext.Request, AccountJsonContext.Default.PasskeyCreationRequest, cancellationToken);
        CredentialCreateOptions options = await passkeys.BeginAddPasskeyAsync(httpContext, user, GetName(body?.PasskeyName, DefaultPasskeyName)!, cancellationToken);

        return Results.Json(options, FidoModelSerializerContext.Default.CredentialCreateOptions);
    }

    // POST api/account/passkeys?userName=
    // Finishes creating the account with the user name, which signs the new user in. Without a user name,
    // finishes adding a passkey to the signed-in user's account.
    public static async Task<IResult> CreatePasskeyAsync(string? userName, HttpContext httpContext, UserService users, PasskeyService passkeys, CancellationToken cancellationToken)
    {
        AuthenticatorAttestationRawResponse? response = await EndpointHelpers.ReadJsonOrNullAsync(
            httpContext.Request,
            AccountJsonContext.Default.AuthenticatorAttestationRawResponse,
            cancellationToken);
        if (response == null)
            return Results.BadRequest();

        PasskeyResult result;
        if (userName != null)
        {
            result = await passkeys.CompleteSignUpAsync(httpContext, userName, response, cancellationToken);
        }
        else
        {
            User? user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
            if (user == null)
                return Results.Unauthorized();

            result = await passkeys.CompleteAddPasskeyAsync(httpContext, user, response, cancellationToken);
        }

        if (result.User == null)
            return EndpointHelpers.Text(StatusCodes.Status400BadRequest, result.Error!);

        if (userName != null)
            await SignInAsync(httpContext, users, result.User, cancellationToken);

        return Results.NoContent();
    }

    // POST api/account/signin/options
    public static async Task<IResult> SignInOptionsAsync(HttpContext httpContext, PasskeyService passkeys, CancellationToken cancellationToken)
    {
        SignInRequest? body = await EndpointHelpers.ReadJsonOrNullAsync(httpContext.Request, AccountJsonContext.Default.SignInRequest, cancellationToken);
        string? userName = body?.UserName?.Trim();

        AssertionOptions? options = string.IsNullOrEmpty(userName) ? null : await passkeys.BeginSignInAsync(httpContext, userName, cancellationToken);
        if (options == null)
            return EndpointHelpers.Text(StatusCodes.Status400BadRequest, $"There's no account named '{userName}'.");

        return Results.Json(options, FidoModelSerializerContext.Default.AssertionOptions);
    }

    // POST api/account/signin
    public static async Task<IResult> SignInAsync(HttpContext httpContext, UserService users, PasskeyService passkeys, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        AuthenticatorAssertionRawResponse? response = await EndpointHelpers.ReadJsonOrNullAsync(
            httpContext.Request,
            AccountJsonContext.Default.AuthenticatorAssertionRawResponse,
            cancellationToken);
        if (response == null)
            return Results.BadRequest();

        PasskeyResult result = await passkeys.CompleteSignInAsync(httpContext, response, cancellationToken);
        if (result.User == null)
        {
            Log.SignInFailed(loggerFactory.CreateLogger(LoggerCategory), FeedActor.Address(httpContext), result.Error!);
            return EndpointHelpers.Text(StatusCodes.Status400BadRequest, result.Error!);
        }

        await SignInAsync(httpContext, users, result.User, cancellationToken);
        return Results.NoContent();
    }

    // POST api/account/signout
    // This only takes the HttpContext, so it's mapped as a RequestDelegate, which can't return a result.
    public static async Task SignOutAsync(HttpContext httpContext)
    {
        AuthenticateResult result = await httpContext.AuthenticateAsync(NuDepotAuthenticationSchemes.User);
        (int Key, string Token)? session = result.Properties == null ? null : UserCookieEvents.GetSession(result.Properties);
        if (result.Succeeded && session != null)
        {
            await httpContext.RequestServices
                .GetRequiredService<UserService>()
                .DeleteSessionAsync(session.Value.Key, httpContext.RequestAborted);
        }

        await httpContext.SignOutAsync(NuDepotAuthenticationSchemes.User);
        httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    // GET api/account/passkeys
    public static async Task<IResult> GetPasskeysAsync(HttpContext httpContext, UserService users, CancellationToken cancellationToken)
    {
        User? user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
        if (user == null)
            return Results.Unauthorized();

        List<UserPasskey> passkeys = await users.GetPasskeysAsync(user.Key, cancellationToken);

        return Results.Json(
            [.. passkeys.Select(p => new PasskeyResponse
            {
                Id = p.Key,
                Name = p.Name,
                CreatedAt = p.CreatedAt,
                LastUsedAt = p.LastUsedAt,
                IsBackedUp = p.IsBackedUp,
            })],
            AccountJsonContext.Default.PasskeyResponseArray);
    }

    // DELETE api/account/passkeys/{id}
    public static async Task<IResult> DeletePasskeyAsync(int id, HttpContext httpContext, UserService users, CancellationToken cancellationToken)
    {
        User? user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
        if (user == null)
            return Results.Unauthorized();

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
        User? user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
        if (user == null)
            return Results.Unauthorized();

        List<UserApiKey> apiKeys = await users.GetApiKeysAsync(user.Key, cancellationToken);

        return Results.Json([.. apiKeys.Select(k => ToResponse(k, key: null))], AccountJsonContext.Default.ApiKeyResponseArray);
    }

    // POST api/account/apikeys
    public static async Task<IResult> CreateApiKeyAsync(HttpContext httpContext, UserService users, CancellationToken cancellationToken)
    {
        User? user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
        if (user == null)
            return Results.Unauthorized();

        ApiKeyCreationRequest? body = await EndpointHelpers.ReadJsonOrNullAsync(httpContext.Request, AccountJsonContext.Default.ApiKeyCreationRequest, cancellationToken);
        string? name = GetName(body?.Name, defaultName: null);
        if (name == null)
            return EndpointHelpers.Text(StatusCodes.Status400BadRequest, "An API key needs a name, like \"Build server\".");

        (UserApiKey? apiKey, string? key) = await users.CreateApiKeyAsync(user.Key, name, cancellationToken);

        return Results.Json(ToResponse(apiKey, key), AccountJsonContext.Default.ApiKeyResponse, statusCode: StatusCodes.Status201Created);
    }

    // DELETE api/account/apikeys/{id}
    public static async Task<IResult> DeleteApiKeyAsync(int id, HttpContext httpContext, UserService users, CancellationToken cancellationToken)
    {
        User? user = await GetSignedInUserAsync(httpContext, users, cancellationToken);
        if (user == null)
            return Results.Unauthorized();

        return await users.DeleteApiKeyAsync(user.Key, id, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }

    /// <summary>
    /// The user signed in with the sign-in cookie. API keys don't count: they can't manage accounts.
    /// </summary>
    internal static async Task<User?> GetSignedInUserAsync(HttpContext httpContext, UserService users, CancellationToken cancellationToken)
        => await GetSignedInUserAsync(await httpContext.AuthenticateAsync(NuDepotAuthenticationSchemes.User), users, cancellationToken);

    private static async Task<User?> GetSignedInUserAsync(AuthenticateResult result, UserService users, CancellationToken cancellationToken)
    {
        int? userKey = result.Succeeded ? FeedClaims.GetUserKey(result.Principal) : null;

        return userKey == null ? null : await users.FindAsync(userKey.Value, cancellationToken);
    }

    private static async Task SignInAsync(HttpContext httpContext, UserService users, User user, CancellationToken cancellationToken)
    {
        ClaimsPrincipal principal = new(FeedClaims.CreateIdentity(user, NuDepotAuthenticationSchemes.User));
        AuthenticationProperties properties = new()
        { IsPersistent = true };

        (UserSession? session, string? token) = await users.CreateSessionAsync(user, cancellationToken);
        UserCookieEvents.SetSession(properties, session.Key, token);

        await httpContext.SignInAsync(NuDepotAuthenticationSchemes.User, principal, properties);
    }

    private static string? GetName(string? name, string? defaultName)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name))
            return defaultName;

        return name.Length > NuDepotDbContext.MaxCredentialNameLength ? name[..NuDepotDbContext.MaxCredentialNameLength] : name;
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

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "A passkey sign-in from {Address} failed: {Reason}")]
        public static partial void SignInFailed(ILogger logger, string address, string reason);
    }
}
