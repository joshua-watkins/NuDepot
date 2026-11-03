using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Feed.Authentication;
using NuGet.Feed.Configuration;
using NuGet.Feed.Entities;
using NuGet.Feed.Extensions;

namespace NuGet.Feed.Users;

/// <summary>
/// The feed's users, their passkeys and their API keys.
/// </summary>
public partial class UserService(
    NuGetFeedContext context,
    SystemTime time,
    IOptionsMonitor<FeedAuthorizationOptions> options,
    ILogger<UserService> logger)
{
    /// <summary>
    /// The start of every user API key, which tells them apart from configured keys.
    /// </summary>
    public const string ApiKeyPrefix = "nf_";

    // Key prefixes are shown to users so they can tell their keys apart.
    private const int ShownApiKeyLength = 10;

    // Recording when keys and passkeys were last used writes to the database, so it's only done this often.
    private static readonly TimeSpan LastUsedPrecision = TimeSpan.FromHours(1);

    // How long the browser has to answer a passkey challenge.
    private static readonly TimeSpan CeremonyLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a user stays signed in after their last request.
    /// </summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(365);

    // How long a session's previous token is still accepted after it's replaced.
    private static readonly TimeSpan PreviousTokenLifetime = TimeSpan.FromMinutes(1);

    private readonly NuGetFeedContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly SystemTime _time = time ?? throw new ArgumentNullException(nameof(time));
    private readonly IOptionsMonitor<FeedAuthorizationOptions> _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<UserService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Whether a user name can be used for a new account: letters, digits, '.', '_', '-' and '@',
    /// starting with a letter or digit.
    /// </summary>
    public static bool IsValidUserName(string? userName)
        => userName != null && UserNamePattern().IsMatch(userName);

    /// <summary>
    /// Whether someone may create an account. The first account can always be created.
    /// </summary>
    public async Task<bool> CanSignUpAsync(CancellationToken cancellationToken)
        => _options.CurrentValue.Users.AllowSignUp || !await AnyAsync(cancellationToken);

    /// <summary>
    /// Whether anyone has an account.
    /// </summary>
    public async Task<bool> AnyAsync(CancellationToken cancellationToken)
        => await Accounts.AnyAsync(cancellationToken);

    /// <summary>
    /// Find an account, but not a sign-up in progress.
    /// </summary>
    public async Task<User?> FindAsync(int userKey, CancellationToken cancellationToken)
        => await Accounts.SingleOrDefaultAsync(u => u.Key == userKey, cancellationToken);

    /// <summary>
    /// Find a user, or a sign-up in progress, with their passkeys.
    /// </summary>
    public async Task<User?> FindByNameAsync(string userName, CancellationToken cancellationToken)
    {
        return await _context.Users
            .Include(u => u.Passkeys)
            .SingleOrDefaultAsync(u => u.UserName == userName, cancellationToken);
    }

    /// <summary>
    /// Start creating an account. Until its first passkey is created, the account is only a sign-up in
    /// progress, which another sign-up for the same user name replaces.
    /// </summary>
    /// <returns>The sign-up, or null if the user name is taken.</returns>
    public async Task<User?> StartSignUpAsync(string userName, byte[] handle, string options, string passkeyName, CancellationToken cancellationToken)
    {
        var now = _time.UtcNow;

        // Sign-ups that were never finished don't keep their user names. Taking a ceremony leaves its
        // expiry, so a sign-up being finished right now is kept.
        await _context.Users
            .Where(u => !u.Passkeys.Any() && u.UserName != userName && u.CeremonyExpiresAt < now)
            .ExecuteDeleteAsync(cancellationToken);

        var user = await FindByNameAsync(userName, cancellationToken);
        if (user?.Passkeys.Count > 0) return null;

        if (user == null)
        {
            user = new User
            {
                UserName = userName,
                Role = _options.CurrentValue.Users.DefaultRole,
                CreatedAt = now,
            };

            _context.Users.Add(user);
        }

        user.Handle = handle;
        SetCeremony(user, PasskeyCeremony.SignUp, options, passkeyName);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (_context.IsUniqueConstraintViolationException(e))
        {
            return null;
        }

        return user;
    }

    /// <summary>
    /// Finish creating an account with its first passkey. The first account is an Admin, so the feed can be set up.
    /// </summary>
    public async Task CompleteSignUpAsync(User user, UserPasskey passkey, CancellationToken cancellationToken)
    {
        // Only one account can be the first.
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var isFirst = !await AnyAsync(cancellationToken);
        user.Role = isFirst ? FeedRole.Admin : _options.CurrentValue.Users.DefaultRole;
        user.CreatedAt = _time.UtcNow;
        user.Passkeys.Add(passkey);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Created user {UserName} with the {Role} role", user.UserName, user.Role);
    }

    // Passkey ceremonies

    /// <summary>
    /// Start a passkey ceremony for the user, replacing the one in progress.
    /// </summary>
    public async Task StartCeremonyAsync(User user, PasskeyCeremony ceremony, string options, string? passkeyName, CancellationToken cancellationToken)
    {
        SetCeremony(user, ceremony, options, passkeyName);

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Take the user's ceremony in progress to answer it, so it can't be answered again. Its options
    /// stay on <paramref name="user"/>.
    /// </summary>
    /// <returns>Whether the user's ceremony was this one, and hadn't expired or been answered.</returns>
    public async Task<bool> TakeCeremonyAsync(User user, PasskeyCeremony ceremony, CancellationToken cancellationToken)
    {
        var options = user.CeremonyOptions;
        if (user.Ceremony != ceremony || options == null || !(user.CeremonyExpiresAt > _time.UtcNow)) return false;

        // Only one request takes it, even if two answer the same challenge at once.
        var taken = await _context.Users
            .Where(u => u.Key == user.Key && u.CeremonyOptions == options)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(u => u.Ceremony, (PasskeyCeremony?)null)
                    .SetProperty(u => u.CeremonyOptions, (string?)null)
                    .SetProperty(u => u.CeremonyPasskeyName, (string?)null),
                cancellationToken);

        return taken == 1;
    }

    private void SetCeremony(User user, PasskeyCeremony ceremony, string options, string? passkeyName)
    {
        user.Ceremony = ceremony;
        user.CeremonyOptions = options;
        user.CeremonyPasskeyName = passkeyName;
        user.CeremonyExpiresAt = _time.UtcNow + CeremonyLifetime;
    }

    // Users who have signed up. The rest are sign-ups in progress.
    private IQueryable<User> Accounts => _context.Users.Where(u => u.Passkeys.Any());

    // Passkeys

    public async Task<List<UserPasskey>> GetPasskeysAsync(int userKey, CancellationToken cancellationToken)
    {
        return await _context.UserPasskeys
            .Where(p => p.UserKey == userKey)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> PasskeyExistsAsync(byte[] credentialId, CancellationToken cancellationToken)
        => await _context.UserPasskeys.AnyAsync(p => p.CredentialId == credentialId, cancellationToken);

    /// <summary>
    /// Find a passkey, with its user, by its credential ID.
    /// </summary>
    public async Task<UserPasskey?> FindPasskeyAsync(byte[] credentialId, CancellationToken cancellationToken)
    {
        return await _context.UserPasskeys
            .Include(p => p.User)
            .SingleOrDefaultAsync(p => p.CredentialId == credentialId, cancellationToken);
    }

    public async Task AddPasskeyAsync(int userKey, UserPasskey passkey, CancellationToken cancellationToken)
    {
        passkey.UserKey = userKey;
        _context.UserPasskeys.Add(passkey);

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Record a sign-in with a passkey: its new signature counter and when it was used.
    /// </summary>
    public async Task RecordSignInAsync(UserPasskey passkey, uint signCount, bool isBackedUp, CancellationToken cancellationToken)
    {
        passkey.SignCount = signCount;
        passkey.IsBackedUp = isBackedUp;
        passkey.LastUsedAt = _time.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Delete one of the user's passkeys, unless it's their last, which they need to sign in.
    /// </summary>
    public async Task<PasskeyDeletionResult> DeletePasskeyAsync(int userKey, int passkeyKey, CancellationToken cancellationToken)
    {
        var passkeys = await GetPasskeysAsync(userKey, cancellationToken);
        var passkey = passkeys.SingleOrDefault(p => p.Key == passkeyKey);

        if (passkey == null) return PasskeyDeletionResult.NotFound;
        if (passkeys.Count == 1) return PasskeyDeletionResult.LastPasskey;

        _context.UserPasskeys.Remove(passkey);
        await _context.SaveChangesAsync(cancellationToken);

        return PasskeyDeletionResult.Deleted;
    }

    // API keys

    public async Task<List<UserApiKey>> GetApiKeysAsync(int userKey, CancellationToken cancellationToken)
    {
        return await _context.UserApiKeys
            .Where(k => k.UserKey == userKey)
            .OrderBy(k => k.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Create an API key for the user. The key itself is only returned here: only its hash is stored.
    /// </summary>
    public async Task<(UserApiKey ApiKey, string Key)> CreateApiKeyAsync(int userKey, string name, CancellationToken cancellationToken)
    {
        var key = ApiKeyPrefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var apiKey = new UserApiKey
        {
            UserKey = userKey,
            Name = name,
            Prefix = key[..ShownApiKeyLength],
            Hash = Hash(key),
            CreatedAt = _time.UtcNow,
        };

        _context.UserApiKeys.Add(apiKey);
        await _context.SaveChangesAsync(cancellationToken);

        return (apiKey, key);
    }

    public async Task<bool> DeleteApiKeyAsync(int userKey, int apiKeyKey, CancellationToken cancellationToken)
    {
        var deleted = await _context.UserApiKeys
            .Where(k => k.UserKey == userKey && k.Key == apiKeyKey)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }

    /// <summary>
    /// Find the user whose API key this is.
    /// </summary>
    public async Task<User?> FindByApiKeyAsync(string key, CancellationToken cancellationToken)
    {
        if (!key.StartsWith(ApiKeyPrefix, StringComparison.Ordinal)) return null;

        var hash = Hash(key);
        var apiKey = await _context.UserApiKeys
            .Include(k => k.User)
            .SingleOrDefaultAsync(k => k.Hash == hash, cancellationToken);

        if (apiKey == null) return null;

        var now = _time.UtcNow;
        if (apiKey.LastUsedAt == null || now - apiKey.LastUsedAt > LastUsedPrecision)
        {
            apiKey.LastUsedAt = now;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return apiKey.User;
    }

    // Sessions

    /// <summary>
    /// Start a session for a browser that signed in.
    /// </summary>
    /// <returns>The session, and its first token for the cookie. Only the token's hash is stored.</returns>
    public async Task<(UserSession Session, string Token)> CreateSessionAsync(int userKey, CancellationToken cancellationToken)
    {
        var now = _time.UtcNow;
        var token = CreateSessionToken();
        var session = new UserSession
        {
            UserKey = userKey,
            TokenHash = Hash(token),
            CreatedAt = now,
            RotatedAt = now,
            ExpiresAt = now + SessionLifetime,
        };

        _context.UserSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);

        return (session, token);
    }

    /// <summary>
    /// Check a session's cookie, and replace its token. A token that was already replaced means
    /// the cookie was copied, so the session is revoked.
    /// </summary>
    public async Task<SessionValidation> ValidateSessionAsync(int sessionKey, string token, CancellationToken cancellationToken)
    {
        var now = _time.UtcNow;
        var session = await _context.UserSessions
            .Include(s => s.User)
            .SingleOrDefaultAsync(s => s.Key == sessionKey, cancellationToken);

        if (session == null || session.ExpiresAt <= now) return SessionValidation.SignedOut;

        var hash = Hash(token);
        if (hash.AsSpan().SequenceEqual(session.TokenHash))
        {
            var newToken = CreateSessionToken();
            var newHash = Hash(newToken);

            // Only one request replaces the token, even if the browser sent several at once.
            var rotated = await _context.UserSessions
                .Where(s => s.Key == sessionKey && s.TokenHash == hash)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(s => s.TokenHash, newHash)
                        .SetProperty(s => s.PreviousTokenHash, hash)
                        .SetProperty(s => s.RotatedAt, now)
                        .SetProperty(s => s.ExpiresAt, now + SessionLifetime),
                    cancellationToken);

            return new SessionValidation(session.User, rotated == 1 ? newToken : null);
        }

        // Requests the browser sent at the same time as the one that replaced the token, and the
        // website's requests to the feed for the browser, have the previous token.
        if (session.PreviousTokenHash != null &&
            hash.AsSpan().SequenceEqual(session.PreviousTokenHash) &&
            now - session.RotatedAt < PreviousTokenLifetime)
        {
            return new SessionValidation(session.User, NewToken: null);
        }

        await _context.UserSessions.Where(s => s.Key == sessionKey).ExecuteDeleteAsync(cancellationToken);

        _logger.LogWarning(
            "A sign-in cookie for {UserName} was used after it was replaced, so it was copied. The session was revoked",
            session.User.UserName);

        return SessionValidation.SignedOut;
    }

    public async Task DeleteSessionAsync(int sessionKey, CancellationToken cancellationToken)
        => await _context.UserSessions.Where(s => s.Key == sessionKey).ExecuteDeleteAsync(cancellationToken);

    private static string CreateSessionToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    // Looking up the hash is safe: the stored hashes don't reveal the keys.
    private static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._@-]{0,63}$")]
    private static partial Regex UserNamePattern();
}

/// <summary>
/// The user of a valid session, and the session's new token if the cookie should be replaced.
/// </summary>
public record SessionValidation(User? User, string? NewToken)
{
    public static readonly SessionValidation SignedOut = new(null, null);
}

public enum PasskeyDeletionResult
{
    Deleted,
    NotFound,

    /// <summary>
    /// The passkey is the user's last, which they need to sign in.
    /// </summary>
    LastPasskey,
}
