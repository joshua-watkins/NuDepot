using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NuDepot.Core;
using NuDepot.Feed.Authentication;
using System.Globalization;
using System.Security.Claims;

namespace NuDepot.Feed.Users;

/// <summary>
/// Writes the sign-in cookie as a JWT, signed with ES256, and reads it back. It carries the user's ID, name and role,
/// and their session's ID and current token. The cookie works as it did before: each request still checks the
/// session in the database, which replaces the token, and a replaced token that's used again revokes the session.
/// </summary>
/// <param name="keys">The keys that sign the cookies.</param>
/// <param name="options">The cookie's options, whose clock says when the token expires.</param>
internal sealed class SessionTokenFormat(SessionSigningKeys keys, CookieAuthenticationOptions options) : ISecureDataFormat<AuthenticationTicket>
{
    /// <summary>
    /// The tokens' issuer and audience: the feed's users.
    /// </summary>
    public const string Issuer = "NuDepot";

    /// <summary>
    /// The claim of the session's ID.
    /// </summary>
    public const string SessionIdClaim = "sid";

    /// <summary>
    /// The claim of the session's current token, which is replaced every few minutes.
    /// </summary>
    public const string SessionTokenClaim = "sit";

    private SessionSigningKeys Keys { get; } = keys;
    private CookieAuthenticationOptions Options { get; } = options;
    private TimeProvider Time => Options.TimeProvider ?? TimeProvider.System;
    private JsonWebTokenHandler Handler { get; } = new() { SetDefaultTimesOnTokenCreation = false, MapInboundClaims = false };

    public string Protect(AuthenticationTicket data) => Protect(data, purpose: null);

    public string Protect(AuthenticationTicket data, string? purpose)
    {
        ClaimsPrincipal principal = data.Principal;
        (int Key, string Token) session = UserCookieEvents.GetSession(data.Properties)
            ?? throw new InvalidOperationException("A sign-in cookie needs a session.");
        DateTimeOffset issuedAt = data.Properties.IssuedUtc ?? Time.GetUtcNow();

        return Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Issuer,
            IssuedAt = issuedAt.UtcDateTime,
            Expires = (data.Properties.ExpiresUtc ?? issuedAt + UserService.SessionLifetime).UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = principal.FindFirstValue(ClaimTypes.NameIdentifier)!,
                [JwtRegisteredClaimNames.Name] = principal.FindFirstValue(ClaimTypes.Name)!,
                ["role"] = principal.FindFirstValue(ClaimTypes.Role)!,
                [SessionIdClaim] = session.Key.ToString(CultureInfo.InvariantCulture),
                [SessionTokenClaim] = session.Token,
            },
            SigningCredentials = new SigningCredentials(Keys.SigningKey, SecurityAlgorithms.EcdsaSha256),
        });
    }

    public AuthenticationTicket? Unprotect(string? protectedText) => Unprotect(protectedText, purpose: null);

    public AuthenticationTicket? Unprotect(string? protectedText, string? purpose)
    {
        if (string.IsNullOrEmpty(protectedText))
            return null;

        TokenValidationResult result = Handler
            .ValidateTokenAsync(protectedText, new TokenValidationParameters
            {
                ValidIssuer = Issuer,
                ValidAudience = Issuer,
                IssuerSigningKeys = Keys.ValidationKeys,
                ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                RequireSignedTokens = true,
                RequireExpirationTime = true,

                // With the cookie's clock, which tests move forward.
                LifetimeValidator = (_, expires, _, _) => expires > Time.GetUtcNow().UtcDateTime,
            })
            .GetAwaiter()
            .GetResult();

        if (!result.IsValid || result.SecurityToken is not JsonWebToken token)
            return null;

        if (!token.TryGetPayloadValue(JwtRegisteredClaimNames.Sub, out string userId) ||
            !token.TryGetPayloadValue(JwtRegisteredClaimNames.Name, out string userName) ||
            !token.TryGetPayloadValue("role", out string roleName) ||
            !Enum.TryParse(roleName, out FeedRole role) ||
            !token.TryGetPayloadValue(SessionIdClaim, out string sessionId) ||
            !int.TryParse(sessionId, NumberStyles.None, CultureInfo.InvariantCulture, out int sessionKey) ||
            !token.TryGetPayloadValue(SessionTokenClaim, out string sessionToken))
            return null;

        // The same claims the feed issues for signed-in users, which ValidatePrincipal replaces with the user's
        // current ones from the database.
        ClaimsIdentity identity = FeedClaims.CreateIdentity(userName, role, NuDepotAuthenticationSchemes.User);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId, ClaimValueTypes.Integer32, ApiKeyAuthenticationHandler.Issuer));

        AuthenticationProperties properties = new()
        {
            IsPersistent = true,
            IssuedUtc = new DateTimeOffset(token.IssuedAt, TimeSpan.Zero),
            ExpiresUtc = new DateTimeOffset(token.ValidTo, TimeSpan.Zero),
        };
        UserCookieEvents.SetSession(properties, sessionKey, sessionToken);

        return new AuthenticationTicket(new ClaimsPrincipal(identity), properties, NuDepotAuthenticationSchemes.User);
    }
}
