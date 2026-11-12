using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using System.Security.Cryptography;

namespace NuDepot.Feed.Users;

/// <summary>
/// The keys that sign the users' sign-in cookies, which are JWTs: ECDSA P-256 keys, stored in the database with their
/// private keys encrypted by ASP.NET Core data protection. The newest signs, and every key that can be decrypted
/// validates, so cookies signed before a new key was made keep working. If no key can be decrypted, because data
/// protection's keys were lost, a new one is made, and users sign in again.
/// </summary>
/// <param name="scopes">Creates the scopes the database is used in.</param>
/// <param name="dataProtection">Encrypts the keys' private keys.</param>
/// <param name="time">The current time.</param>
/// <param name="logger">The logger.</param>
internal sealed class SessionSigningKeys(IServiceScopeFactory scopes, IDataProtectionProvider dataProtection, TimeProvider time, ILogger<SessionSigningKeys> logger) : IDisposable
{
    private const string ProtectorPurpose = "NuDepot.Feed.Users.SessionSigningKeys";

    private IServiceScopeFactory Scopes { get; } = scopes;
    private IDataProtector Protector { get; } = dataProtection.CreateProtector(ProtectorPurpose);
    private TimeProvider Time { get; } = time;
    private ILogger<SessionSigningKeys> Logger { get; } = logger;
    private SemaphoreSlim Lock { get; } = new(1, 1);
    private LoadedKeys? Loaded { get; set; }

    /// <summary>
    /// The key that signs new cookies.
    /// </summary>
    public ECDsaSecurityKey SigningKey => GetLoaded().SigningKey;

    /// <summary>
    /// The keys that cookies may be signed with.
    /// </summary>
    public IReadOnlyList<SecurityKey> ValidationKeys => GetLoaded().ValidationKeys;

    /// <summary>
    /// Loads the keys, and makes one if there isn't one. The application does it when it starts, after migrating the
    /// database, so requests don't wait for it.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (Loaded != null)
            return;

        await Lock.WaitAsync(cancellationToken);
        try
        {
            Loaded ??= await LoadOrCreateAsync(cancellationToken);
        }
        finally
        {
            Lock.Release();
        }
    }

    public void Dispose()
    {
        if (Loaded != null)
        {
            foreach (ECDsaSecurityKey key in Loaded.ValidationKeys.Cast<ECDsaSecurityKey>())
                key.ECDsa.Dispose();
        }

        Lock.Dispose();
    }

    // Cookies are read and written synchronously. An application that hasn't started, like one running a command,
    // loads the keys the first time it needs them.
    private LoadedKeys GetLoaded()
    {
        if (Loaded == null)
        {
            LoadAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        return Loaded!;
    }

    private async Task<LoadedKeys> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = Scopes.CreateScope();
        NuDepotContext context = scope.ServiceProvider.GetRequiredService<NuDepotContext>();

        List<SessionSigningKey> rows = await context.SessionSigningKeys
            .OrderByDescending(k => k.Key)
            .ToListAsync(cancellationToken);

        List<ECDsaSecurityKey> keys = [];
        foreach (SessionSigningKey row in rows)
        {
            ECDsaSecurityKey? key = TryLoad(row);
            if (key != null)
                keys.Add(key);
        }

        if (keys.Count == 0)
        {
            SessionSigningKey row = Create();
            await context.SessionSigningKeys.ExecuteInsertAsync(row, cancellationToken);
            keys.Add(TryLoad(row)!);

            if (rows.Count > 0)
                Logger.LogWarning("None of the keys that sign sign-in cookies could be decrypted, so a new one was made, and users have to sign in again");
            else
                Logger.LogInformation("Made the key that signs sign-in cookies");
        }

        return new LoadedKeys(keys[0], keys);
    }

    private SessionSigningKey Create()
    {
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new SessionSigningKey
        {
            KeyId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
            ProtectedPrivateKey = Protector.Protect(ecdsa.ExportPkcs8PrivateKey()),
            CreatedAt = Time.GetUtcNow().UtcDateTime,
        };
    }

    private ECDsaSecurityKey? TryLoad(SessionSigningKey row)
    {
        try
        {
            ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(Protector.Unprotect(row.ProtectedPrivateKey), out _);
            return new ECDsaSecurityKey(ecdsa) { KeyId = row.KeyId };
        }
        catch (CryptographicException e)
        {
            Logger.LogWarning(e, "The key {KeyId} that signs sign-in cookies can't be decrypted", row.KeyId);
            return null;
        }
    }

    private sealed record LoadedKeys(ECDsaSecurityKey SigningKey, IReadOnlyList<SecurityKey> ValidationKeys);
}

/// <summary>
/// Loads the keys that sign sign-in cookies when the application starts.
/// </summary>
internal sealed class SessionSigningKeysLoader(SessionSigningKeys keys) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => keys.LoadAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
