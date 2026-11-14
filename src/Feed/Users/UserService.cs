using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NuDepot.Feed.Users;

/// <summary>
/// The feed's users, their passkeys and their API keys.
/// </summary>
internal partial class UserService(ILogger<UserService> logger, NuDepotDbContext context, TimeProvider time, IOptionsMonitor<UserOptions> options, IHttpContextAccessor httpContextAccessor)
{
    /// <summary>
    /// The start of every user API key, which tells them apart from configured keys.
    /// </summary>
    public const string ApiKeyPrefix = "nd_";

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

    // How long a session's token is used before it's replaced, by the next request after that.
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    // How long a session's previous token is still accepted after it's replaced.
    private static readonly TimeSpan PreviousTokenLifetime = TimeSpan.FromMinutes(1);

    private ILogger Logger { get; } = logger;
    private NuDepotDbContext Context { get; } = context;
    private TimeProvider Time { get; } = time;
    private IOptionsMonitor<UserOptions> Options { get; } = options;
    private IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;

    // Who's making the change, and from where, for the logs.
    private string Actor => FeedActor.Describe(HttpContextAccessor.HttpContext);
    private string Address => HttpContextAccessor.HttpContext is { } context ? FeedActor.Address(context) : "an unknown address";

    /// <summary>
    /// Whether a user name can be used for a new account: letters, digits, '.', '_', '-' and '@',
    /// starting with a letter or digit. It can't be a role's name, like "ReadWrite", which names the feed's API
    /// key for the role in a package source's credentials.
    /// </summary>
    public static bool IsValidUserName(string? userName)
        => userName != null && UserNamePattern().IsMatch(userName) && FeedAccess.GetRole(userName) == null;

    /// <summary>
    /// Whether someone may create an account. The first account can always be created.
    /// </summary>
    public async Task<bool> CanSignUpAsync(CancellationToken cancellationToken)
        => Options.CurrentValue.AllowSignUp || !await AnyAsync(cancellationToken);

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
        return await Context.Users
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
        DateTime now = Time.GetUtcNow().UtcDateTime;

        // Sign-ups that were never finished don't keep their user names. Taking a ceremony leaves its
        // expiry, so a sign-up being finished right now is kept.
        await Context.Users
            .Where(u => !u.Passkeys.Any() && u.UserName != userName && u.CeremonyExpiresAt < now)
            .ExecuteDeleteAsync(cancellationToken);

        User? user = await FindByNameAsync(userName, cancellationToken);
        if (user?.Passkeys.Count > 0)
            return null;

        if (user != null)
        {
            user.Handle = handle;
            SetCeremony(user, PasskeyCeremony.SignUp, options, passkeyName);

            // Unless the sign-up was finished meanwhile.
            int key = user.Key;
            int updated = await Context.Users
                .Where(u => u.Key == key && !u.Passkeys.Any())
                .ExecuteUpdateAsync(setters => SetCeremony(setters.SetProperty(u => u.Handle, handle), user), cancellationToken);

            return updated == 1 ? user : null;
        }

        user = new User
        {
            UserName = userName,
            Role = Options.CurrentValue.DefaultRole,
            CreatedAt = now,
            Handle = handle,
        };
        SetCeremony(user, PasskeyCeremony.SignUp, options, passkeyName);
        try
        {
            await Context.Users.ExecuteInsertAsync(user, cancellationToken);
        }
        catch (DbUpdateException e) when (NuDepotDbContext.IsUniqueConstraintViolationException(e))
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
        await using IDbContextTransaction transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        bool isFirst = !await AnyAsync(cancellationToken);
        FeedRole role = isFirst ? FeedRole.Admin : Options.CurrentValue.DefaultRole;
        DateTime now = Time.GetUtcNow().UtcDateTime;

        int key = user.Key;
        await Context.Users
            .Where(u => u.Key == key)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.Role, role).SetProperty(u => u.CreatedAt, now), cancellationToken);

        passkey.UserKey = key;
        await Context.UserPasskeys.ExecuteInsertAsync(passkey, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        user.Role = role;
        user.CreatedAt = now;
        user.Passkeys.Add(passkey);

        Log.UserCreated(Logger, Actor, user.UserName, user.Role);
    }

    // Passkey ceremonies

    /// <summary>
    /// Start a passkey ceremony for the user, replacing the one in progress.
    /// </summary>
    public async Task StartCeremonyAsync(User user, PasskeyCeremony ceremony, string options, string? passkeyName, CancellationToken cancellationToken)
    {
        SetCeremony(user, ceremony, options, passkeyName);

        int key = user.Key;
        await Context.Users
            .Where(u => u.Key == key)
            .ExecuteUpdateAsync(setters => SetCeremony(setters, user), cancellationToken);
    }

    /// <summary>
    /// Take the user's ceremony in progress to answer it, so it can't be answered again. Its options
    /// stay on <paramref name="user"/>.
    /// </summary>
    /// <returns>Whether the user's ceremony was this one, and hadn't expired or been answered.</returns>
    public async Task<bool> TakeCeremonyAsync(User user, PasskeyCeremony ceremony, CancellationToken cancellationToken)
    {
        string? options = user.CeremonyOptions;
        if (user.Ceremony != ceremony || options == null || !(user.CeremonyExpiresAt > Time.GetUtcNow().UtcDateTime))
            return false;

        // Only one request takes it, even if two answer the same challenge at once.
        int taken = await Context.Users
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
        user.CeremonyExpiresAt = Time.GetUtcNow().UtcDateTime + CeremonyLifetime;
    }

    // Saves the ceremony that SetCeremony set on the user.
    private static UpdateSettersBuilder<User> SetCeremony(UpdateSettersBuilder<User> setters, User user)
    {
        PasskeyCeremony? ceremony = user.Ceremony;
        string? options = user.CeremonyOptions;
        string? passkeyName = user.CeremonyPasskeyName;
        DateTime? expiresAt = user.CeremonyExpiresAt;

        return setters
            .SetProperty(u => u.Ceremony, ceremony)
            .SetProperty(u => u.CeremonyOptions, options)
            .SetProperty(u => u.CeremonyPasskeyName, passkeyName)
            .SetProperty(u => u.CeremonyExpiresAt, expiresAt);
    }

    // Users who have signed up. The rest are sign-ups in progress.
    private IQueryable<User> Accounts => Context.Users.Where(u => u.Passkeys.Any());

    // Passkeys

    /// <summary>
    /// Gets a user's passkeys, oldest first.
    /// </summary>
    /// <param name="userKey">The user's key.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The passkeys.</returns>
    public async Task<List<UserPasskey>> GetPasskeysAsync(int userKey, CancellationToken cancellationToken)
    {
        return await Context.UserPasskeys
            .Where(p => p.UserKey == userKey)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Whether any user has a passkey with the credential ID.
    /// </summary>
    /// <param name="credentialId">The WebAuthn credential ID.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>True if the credential is already registered.</returns>
    public async Task<bool> PasskeyExistsAsync(byte[] credentialId, CancellationToken cancellationToken)
        => await Context.UserPasskeys.AnyAsync(p => p.CredentialId == credentialId, cancellationToken);

    /// <summary>
    /// Find a passkey, with its user, by its credential ID.
    /// </summary>
    public async Task<UserPasskey?> FindPasskeyAsync(byte[] credentialId, CancellationToken cancellationToken)
    {
        return await Context.UserPasskeys
            .Include(p => p.User)
            .SingleOrDefaultAsync(p => p.CredentialId == credentialId, cancellationToken);
    }

    /// <summary>
    /// Adds a passkey to a user's account.
    /// </summary>
    /// <param name="userKey">The user's key.</param>
    /// <param name="passkey">The new passkey.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>A task that completes when the passkey is saved.</returns>
    public async Task AddPasskeyAsync(int userKey, UserPasskey passkey, CancellationToken cancellationToken)
    {
        passkey.UserKey = userKey;
        await Context.UserPasskeys.ExecuteInsertAsync(passkey, cancellationToken);

        Log.PasskeyAdded(Logger, Actor, passkey.Name);
    }

    /// <summary>
    /// Record a sign-in with a passkey: its new signature counter and when it was used.
    /// </summary>
    public async Task RecordSignInAsync(UserPasskey passkey, uint signCount, bool isBackedUp, CancellationToken cancellationToken)
    {
        DateTime now = Time.GetUtcNow().UtcDateTime;
        int key = passkey.Key;
        await Context.UserPasskeys
            .Where(p => p.Key == key)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.SignCount, signCount)
                    .SetProperty(p => p.IsBackedUp, isBackedUp)
                    .SetProperty(p => p.LastUsedAt, now),
                cancellationToken);

        passkey.SignCount = signCount;
        passkey.IsBackedUp = isBackedUp;
        passkey.LastUsedAt = now;
    }

    /// <summary>
    /// Delete one of the user's passkeys, unless it's their last, which they need to sign in.
    /// </summary>
    public async Task<PasskeyDeletionResult> DeletePasskeyAsync(int userKey, int passkeyKey, CancellationToken cancellationToken)
    {
        List<UserPasskey> passkeys = await GetPasskeysAsync(userKey, cancellationToken);
        UserPasskey? passkey = passkeys.SingleOrDefault(p => p.Key == passkeyKey);

        if (passkey == null)
            return PasskeyDeletionResult.NotFound;
        if (passkeys.Count == 1)
            return PasskeyDeletionResult.LastPasskey;

        // Still not their last, if another of their passkeys was deleted meanwhile.
        int deleted = await Context.UserPasskeys
            .Where(p => p.Key == passkeyKey && p.UserKey == userKey && Context.UserPasskeys.Count(o => o.UserKey == userKey) > 1)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted != 1)
            return PasskeyDeletionResult.LastPasskey;

        Log.PasskeyDeleted(Logger, Actor, passkey.Name);
        return PasskeyDeletionResult.Deleted;
    }

    // API keys

    /// <summary>
    /// Gets a user's API keys, oldest first.
    /// </summary>
    /// <param name="userKey">The user's key.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The API keys, without their secrets.</returns>
    public async Task<List<UserApiKey>> GetApiKeysAsync(int userKey, CancellationToken cancellationToken)
    {
        return await Context.UserApiKeys
            .Where(k => k.UserKey == userKey)
            .OrderBy(k => k.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Create an API key for the user. The key itself is only returned here: only its hash is stored.
    /// </summary>
    public async Task<(UserApiKey ApiKey, string Key)> CreateApiKeyAsync(int userKey, string name, CancellationToken cancellationToken)
    {
        string key = ApiKeyPrefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        UserApiKey apiKey = new()
        {
            UserKey = userKey,
            Name = name,
            Prefix = key[..ShownApiKeyLength],
            Hash = Hash(key),
            CreatedAt = Time.GetUtcNow().UtcDateTime,
        };

        await Context.UserApiKeys.ExecuteInsertAsync(apiKey, cancellationToken);

        Log.ApiKeyCreated(Logger, Actor, name, apiKey.Prefix);
        return (apiKey, key);
    }

    /// <summary>
    /// Revokes one of a user's API keys.
    /// </summary>
    /// <param name="userKey">The user's key.</param>
    /// <param name="apiKeyKey">The API key's key.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>False if the user doesn't have the key.</returns>
    public async Task<bool> DeleteApiKeyAsync(int userKey, int apiKeyKey, CancellationToken cancellationToken)
    {
        int deleted = await Context.UserApiKeys
            .Where(k => k.UserKey == userKey && k.Key == apiKeyKey)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
            return false;

        Log.ApiKeyDeleted(Logger, Actor, apiKeyKey);
        return true;
    }

    /// <summary>
    /// Find the user whose API key this is.
    /// </summary>
    public async Task<User?> FindByApiKeyAsync(string key, CancellationToken cancellationToken)
    {
        if (!key.StartsWith(ApiKeyPrefix, StringComparison.Ordinal))
            return null;

        byte[] hash = Hash(key);
        UserApiKey? apiKey = await Context.UserApiKeys
            .Include(k => k.User)
            .SingleOrDefaultAsync(k => k.Hash == hash, cancellationToken);

        if (apiKey == null)
            return null;

        DateTime now = Time.GetUtcNow().UtcDateTime;
        if (apiKey.LastUsedAt == null || now - apiKey.LastUsedAt > LastUsedPrecision)
        {
            int apiKeyKey = apiKey.Key;
            await Context.UserApiKeys
                .Where(k => k.Key == apiKeyKey)
                .ExecuteUpdateAsync(setters => setters.SetProperty(k => k.LastUsedAt, now), cancellationToken);
            apiKey.LastUsedAt = now;
        }

        return apiKey.User;
    }

    // Sessions

    /// <summary>
    /// Start a session for a browser that signed in.
    /// </summary>
    /// <returns>The session, and its first token for the cookie. Only the token's hash is stored.</returns>
    public async Task<(UserSession Session, string Token)> CreateSessionAsync(User user, CancellationToken cancellationToken)
    {
        int userKey = user.Key;
        DateTime now = Time.GetUtcNow().UtcDateTime;
        string token = CreateSessionToken();
        UserSession session = new()
        {
            UserKey = userKey,
            TokenHash = Hash(token),
            CreatedAt = now,
            RotatedAt = now,
            ExpiresAt = now + SessionLifetime,
        };

        await Context.UserSessions.ExecuteInsertAsync(session, cancellationToken);

        Log.SignedIn(Logger, user.UserName, Address);
        return (session, token);
    }

    /// <summary>
    /// Check a session's cookie, and replace its token every few minutes. A token that was already replaced, after
    /// the browser used the new one, means the cookie was copied, so the session is revoked.
    /// </summary>
    public async Task<SessionValidation> ValidateSessionAsync(int sessionKey, string token, CancellationToken cancellationToken)
    {
        DateTime now = Time.GetUtcNow().UtcDateTime;
        UserSession? session = await Context.UserSessions
            .Include(s => s.User)
            .SingleOrDefaultAsync(s => s.Key == sessionKey, cancellationToken);

        if (session == null || session.ExpiresAt <= now)
            return SessionValidation.SignedOut;

        byte[] hash = Hash(token);
        if (hash.AsSpan().SequenceEqual(session.TokenHash))
        {
            if (now - session.RotatedAt >= TokenLifetime)
                return new SessionValidation(session.User, await RotateAsync(sessionKey, hash, hash, onlyIfUnused: false, now, cancellationToken));

            // The browser has the cookie with this token, so an older cookie is a copy.
            if (!session.IsTokenUsed)
            {
                await Context.UserSessions
                    .Where(s => s.Key == sessionKey && s.TokenHash == hash)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.IsTokenUsed, true), cancellationToken);
            }

            return new SessionValidation(session.User, NewToken: null);
        }

        bool isPrevious = session.PreviousTokenHash != null && hash.AsSpan().SequenceEqual(session.PreviousTokenHash);

        // Requests the browser sent at the same time as the one that replaced the token, and the
        // website's requests to the feed for the browser, have the previous token.
        if (isPrevious && now - session.RotatedAt < PreviousTokenLifetime)
            return new SessionValidation(session.User, NewToken: null);

        // The browser never sent the current token: the response with its cookie didn't arrive, like when the user
        // left the page while it loaded. The browser gets another.
        if (isPrevious && !session.IsTokenUsed)
            return new SessionValidation(session.User, await RotateAsync(sessionKey, session.TokenHash, hash, onlyIfUnused: true, now, cancellationToken));

        await Context.UserSessions
            .Where(s => s.Key == sessionKey)
            .ExecuteDeleteAsync(cancellationToken);

        Log.CopiedSessionRevoked(Logger, session.User.UserName);

        return SessionValidation.SignedOut;
    }

    /// <summary>
    /// Signs a browser out, by deleting its session.
    /// </summary>
    /// <param name="sessionKey">The session's key.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>A task that completes when the session is deleted.</returns>
    public async Task DeleteSessionAsync(int sessionKey, CancellationToken cancellationToken)
    {
        await Context.UserSessions
            .Where(s => s.Key == sessionKey)
            .ExecuteDeleteAsync(cancellationToken);

        Log.SignedOut(Logger, Actor);
    }

    /// <summary>
    /// Gets the user's signed-in browsers that haven't expired, most recently used first.
    /// </summary>
    public async Task<List<UserSession>> GetSessionsAsync(int userKey, CancellationToken cancellationToken)
    {
        DateTime now = Time.GetUtcNow().UtcDateTime;
        return await Context.UserSessions
            .Where(s => s.UserKey == userKey && s.ExpiresAt > now)
            .OrderByDescending(s => s.RotatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Signs one of the user's browsers out.
    /// </summary>
    /// <returns>False if the user doesn't have the session.</returns>
    public async Task<bool> DeleteSessionAsync(int userKey, int sessionKey, CancellationToken cancellationToken)
    {
        int deleted = await Context.UserSessions
            .Where(s => s.UserKey == userKey && s.Key == sessionKey)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
            return false;

        Log.SessionDeleted(Logger, Actor, sessionKey);
        return true;
    }

    // Replaces the session's current token, whose hash is currentHash. The previous token becomes the one the browser
    // sent: the current one, or the previous one if the browser never got the current one.
    private async Task<string?> RotateAsync(int sessionKey, byte[] currentHash, byte[] sentHash, bool onlyIfUnused, DateTime now, CancellationToken cancellationToken)
    {
        string newToken = CreateSessionToken();
        byte[] newHash = Hash(newToken);

        // Only one request replaces the token, even if the browser sent several at once.
        int rotated = await Context.UserSessions
            .Where(s => s.Key == sessionKey && s.TokenHash == currentHash && (!onlyIfUnused || !s.IsTokenUsed))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.TokenHash, newHash)
                    .SetProperty(s => s.PreviousTokenHash, sentHash)
                    .SetProperty(s => s.IsTokenUsed, false)
                    .SetProperty(s => s.RotatedAt, now)
                    .SetProperty(s => s.ExpiresAt, now + SessionLifetime),
                cancellationToken);

        return rotated == 1 ? newToken : null;
    }

    private static string CreateSessionToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    // Administration

    /// <summary>
    /// Gets every user who has signed up, by name, with how many passkeys, API keys and signed-in browsers they have.
    /// </summary>
    public async Task<List<UserSummary>> GetUsersAsync(CancellationToken cancellationToken)
    {
        DateTime now = Time.GetUtcNow().UtcDateTime;
        return await Accounts
            .OrderBy(u => u.UserName)
            .Select(u => new UserSummary(
                u.Key,
                u.UserName,
                u.Role,
                u.CreatedAt,
                u.Passkeys.Count,
                u.ApiKeys.Count,
                u.Sessions.Count(s => s.ExpiresAt > now),
                u.Passkeys.Max(p => p.LastUsedAt)))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Changes a user's role. The feed's last Admin stays an Admin, so someone can still administer it.
    /// </summary>
    public async Task<UserChangeResult> SetRoleAsync(int userKey, FeedRole role, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        User? user = await FindAsync(userKey, cancellationToken);
        if (user == null)
            return UserChangeResult.NotFound;
        if (user.Role == FeedRole.Admin && role != FeedRole.Admin && await IsLastAdminAsync(userKey, cancellationToken))
            return UserChangeResult.LastAdmin;

        await Context.Users
            .Where(u => u.Key == userKey)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.Role, role), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        Log.RoleChanged(Logger, Actor, user.UserName, user.Role, role);
        return UserChangeResult.Changed;
    }

    /// <summary>
    /// Deletes a user, with their passkeys, API keys and signed-in browsers. The feed's last Admin can't be deleted.
    /// </summary>
    public async Task<UserChangeResult> DeleteUserAsync(int userKey, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        User? user = await FindAsync(userKey, cancellationToken);
        if (user == null)
            return UserChangeResult.NotFound;
        if (user.Role == FeedRole.Admin && await IsLastAdminAsync(userKey, cancellationToken))
            return UserChangeResult.LastAdmin;

        await Context.UserSessions.Where(s => s.UserKey == userKey).ExecuteDeleteAsync(cancellationToken);
        await Context.UserApiKeys.Where(k => k.UserKey == userKey).ExecuteDeleteAsync(cancellationToken);
        await Context.UserPasskeys.Where(p => p.UserKey == userKey).ExecuteDeleteAsync(cancellationToken);
        await Context.Users.Where(u => u.Key == userKey).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        Log.UserDeleted(Logger, Actor, user.UserName);
        return UserChangeResult.Changed;
    }

    /// <summary>
    /// Signs a user out of every browser.
    /// </summary>
    /// <returns>False if there's no such user.</returns>
    public async Task<bool> DeleteSessionsAsync(int userKey, CancellationToken cancellationToken)
    {
        User? user = await FindAsync(userKey, cancellationToken);
        if (user == null)
            return false;

        await Context.UserSessions
            .Where(s => s.UserKey == userKey)
            .ExecuteDeleteAsync(cancellationToken);

        Log.SessionsRevoked(Logger, Actor, user.UserName);
        return true;
    }

    /// <summary>
    /// Revokes all of a user's API keys.
    /// </summary>
    /// <returns>False if there's no such user.</returns>
    public async Task<bool> DeleteApiKeysAsync(int userKey, CancellationToken cancellationToken)
    {
        User? user = await FindAsync(userKey, cancellationToken);
        if (user == null)
            return false;

        await Context.UserApiKeys
            .Where(k => k.UserKey == userKey)
            .ExecuteDeleteAsync(cancellationToken);

        Log.ApiKeysRevoked(Logger, Actor, user.UserName);
        return true;
    }

    private async Task<bool> IsLastAdminAsync(int userKey, CancellationToken cancellationToken)
        => !await Accounts.AnyAsync(u => u.Role == FeedRole.Admin && u.Key != userKey, cancellationToken);

    // Looking up the hash is safe: the stored hashes don't reveal the keys.
    private static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._@-]{0,63}$")]
    private static partial Regex UserNamePattern();

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "{Actor} created the user {UserName}, with the {Role} role")]
        public static partial void UserCreated(ILogger logger, string actor, string userName, FeedRole role);

        [LoggerMessage(Level = LogLevel.Information, Message = "{UserName} signed in from {Address}")]
        public static partial void SignedIn(ILogger logger, string userName, string address);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Actor} signed out")]
        public static partial void SignedOut(ILogger logger, string actor);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Actor} signed out another browser, with the session {SessionId}")]
        public static partial void SessionDeleted(ILogger logger, string actor, int sessionId);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Actor} added the passkey {PasskeyName}")]
        public static partial void PasskeyAdded(ILogger logger, string actor, string passkeyName);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Actor} removed the passkey {PasskeyName}")]
        public static partial void PasskeyDeleted(ILogger logger, string actor, string passkeyName);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Actor} created the API key {ApiKeyName}, which starts with {ApiKeyPrefix}")]
        public static partial void ApiKeyCreated(ILogger logger, string actor, string apiKeyName, string apiKeyPrefix);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Actor} revoked their API key {ApiKeyId}")]
        public static partial void ApiKeyDeleted(ILogger logger, string actor, int apiKeyId);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Actor} signed {UserName} out of every browser")]
        public static partial void SessionsRevoked(ILogger logger, string actor, string userName);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Actor} revoked all of {UserName}'s API keys")]
        public static partial void ApiKeysRevoked(ILogger logger, string actor, string userName);

        [LoggerMessage(Level = LogLevel.Warning, Message = "A sign-in cookie for {UserName} was used after it was replaced, so it was copied. The session was revoked")]
        public static partial void CopiedSessionRevoked(ILogger logger, string userName);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Actor} changed {UserName}'s role from {OldRole} to {Role}")]
        public static partial void RoleChanged(ILogger logger, string actor, string userName, FeedRole oldRole, FeedRole role);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Actor} deleted the user {UserName}")]
        public static partial void UserDeleted(ILogger logger, string actor, string userName);
    }
}
