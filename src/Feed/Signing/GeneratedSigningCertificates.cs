using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NuDepot.Feed.Signing;

/// <summary>
/// The size of the RSA keys of generated signing certificates. Tests use smaller keys, which generate faster.
/// </summary>
/// <param name="Bits">The key size, in bits.</param>
internal sealed record GeneratedCertificateKeySize(int Bits)
{
    /// <summary>
    /// The size the feed uses.
    /// </summary>
    public static GeneratedCertificateKeySize Default { get; } = new(8192);
}

/// <summary>
/// The outcome of replacing this year's generated certificate early.
/// </summary>
/// <param name="Fingerprint">The new certificate's fingerprint.</param>
/// <param name="ExpiresAt">When the new certificate expires.</param>
/// <param name="Revoked">The fingerprints of the certificates it replaced.</param>
internal sealed record SigningCertificateRotation(string Fingerprint, DateTimeOffset ExpiresAt, IReadOnlyList<string> Revoked);

/// <summary>
/// Self-signed signing certificates the feed generates, one for each year, when signing is on without a configured
/// certificate (<see cref="Configuration.RepositorySigningOptions.GeneratesCertificates"/>). They're RSA keys signed with SHA-512, stored
/// in the database with their private keys encrypted by ASP.NET Core data protection.
/// </summary>
/// <remarks>
/// A year's certificate is generated when the feed first needs it that year, signs packages from then, and expires at
/// the end of the year. Packages signed with an older one are re-signed when they're downloaded. A certificate
/// replaced early, like after its key leaked, is revoked. If a certificate can't be decrypted, because data
/// protection's keys changed, a new one replaces it. The database is checked for a newer certificate every minute,
/// so a replacement made by the "rotate signing-certificate" command is picked up.
/// </remarks>
/// <param name="scopes">Creates the scopes the database is used in.</param>
/// <param name="dataProtection">Encrypts the certificates' private keys.</param>
/// <param name="time">The current time.</param>
/// <param name="keySize">The size of the certificates' keys.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class GeneratedSigningCertificates(IServiceScopeFactory scopes, IDataProtectionProvider dataProtection, TimeProvider time, GeneratedCertificateKeySize keySize, ILogger<GeneratedSigningCertificates> logger) : ISigningCertificateSource, IDisposable
{
    private const string ProtectorPurpose = "NuDepot.Feed.Signing.GeneratedSigningCertificates";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

    private IServiceScopeFactory Scopes { get; } = scopes;
    private IDataProtector Protector { get; } = dataProtection.CreateProtector(ProtectorPurpose);
    private TimeProvider Time { get; } = time;
    private int KeySize { get; } = keySize.Bits;
    private ILogger<GeneratedSigningCertificates> Logger { get; } = logger;
    private SemaphoreSlim Lock { get; } = new(1, 1);
    private ConcurrentDictionary<string, X509Certificate2> PublicCertificates { get; } = new(StringComparer.OrdinalIgnoreCase);
    private Signer? Current { get; set; }

    public NuGet.Common.HashAlgorithmName HashAlgorithm => NuGet.Common.HashAlgorithmName.SHA512;

    public bool AllowUntrustedRoot => true;

    /// <summary>
    /// When a year's certificate expires: the end of the year.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <returns>The last second of the year, in UTC.</returns>
    internal static DateTimeOffset ExpiresAt(int year) => new DateTimeOffset(year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(-1);

    public async Task<X509Certificate2> GetSigningCertificateAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = Time.GetUtcNow();
        if (IsReady(Current, now))
            return Current!.Certificate;

        await Lock.WaitAsync(cancellationToken);
        try
        {
            if (!IsReady(Current, now))
                Current = await PrepareAsync(now, Current, cancellationToken);

            return Current!.Certificate;
        }
        finally
        {
            Lock.Release();
        }
    }

    public async Task<IReadOnlyList<X509Certificate2>> GetAdvertisedCertificatesAsync(CancellationToken cancellationToken)
    {
        // Makes sure this year's certificate exists.
        await GetSigningCertificateAsync(cancellationToken);

        DateTime now = Time.GetUtcNow().UtcDateTime;
        using IServiceScope scope = Scopes.CreateScope();
        NuDepotContext context = scope.ServiceProvider.GetRequiredService<NuDepotContext>();

        var rows = await context.SigningCertificates
            .Where(c => c.ExpiresAt > now && c.RevokedAt == null)
            .OrderBy(c => c.Year)
            .ThenBy(c => c.Key)
            .Select(c => new { c.Fingerprint, c.Certificate })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => PublicCertificates.GetOrAdd(row.Fingerprint, _ => X509CertificateLoader.LoadCertificate(row.Certificate)))];
    }

    public async Task<X509Certificate2?> FindCertificateAsync(string fingerprint, CancellationToken cancellationToken)
    {
        if (PublicCertificates.TryGetValue(fingerprint, out X509Certificate2? cached))
            return cached;

        using IServiceScope scope = Scopes.CreateScope();
        NuDepotContext context = scope.ServiceProvider.GetRequiredService<NuDepotContext>();

        string lowercase = fingerprint.ToLowerInvariant();
        byte[]? certificate = await context.SigningCertificates
            .Where(c => c.Fingerprint == lowercase)
            .Select(c => c.Certificate)
            .FirstOrDefaultAsync(cancellationToken);

        return certificate == null ? null : PublicCertificates.GetOrAdd(lowercase, _ => X509CertificateLoader.LoadCertificate(certificate));
    }

    /// <summary>
    /// Replaces this year's certificate with a new one that expires at the end of the year, like after its key
    /// leaked. The certificates it replaces are revoked: they aren't advertised any more, and packages they signed
    /// are re-signed when they're downloaded.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The new certificate and the revoked ones.</returns>
    internal async Task<SigningCertificateRotation> RotateAsync(CancellationToken cancellationToken)
    {
        await Lock.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset now = Time.GetUtcNow();
            using IServiceScope scope = Scopes.CreateScope();
            NuDepotContext context = scope.ServiceProvider.GetRequiredService<NuDepotContext>();

            // The key is generated first, so the transaction isn't held while it is.
            SigningCertificate row = Create(now.Year, now);

            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            IQueryable<SigningCertificate> current = context.SigningCertificates.Where(c => c.Year == now.Year && c.RevokedAt == null);
            List<string> replaced = await current
                .Select(c => c.Fingerprint)
                .ToListAsync(cancellationToken);

            DateTime revokedAt = now.UtcDateTime;
            await current.ExecuteUpdateAsync(setters => setters.SetProperty(c => c.RevokedAt, revokedAt), cancellationToken);
            await context.SigningCertificates.ExecuteInsertAsync(row, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            foreach (string fingerprint in replaced)
                Log.Revoked(Logger, fingerprint);
            LogGenerated(row);

            Signer? previous = Current;
            Current = new(now.Year, row.Fingerprint, Load(row), now);
            previous?.Certificate.Dispose();

            return new(row.Fingerprint, new DateTimeOffset(row.ExpiresAt, TimeSpan.Zero), replaced);
        }
        finally
        {
            Lock.Release();
        }
    }

    public void Dispose()
    {
        Current?.Certificate.Dispose();
        foreach (X509Certificate2 certificate in PublicCertificates.Values)
            certificate.Dispose();
        Lock.Dispose();
    }

    /// <summary>
    /// Whether the signer is this year's, and was checked against the database recently.
    /// </summary>
    private static bool IsReady(Signer? signer, DateTimeOffset now)
        => signer != null && signer.Year == now.Year && now - signer.CheckedAt < RefreshInterval;

    private async Task<Signer> PrepareAsync(DateTimeOffset now, Signer? previous, CancellationToken cancellationToken)
    {
        using IServiceScope scope = Scopes.CreateScope();
        NuDepotContext context = scope.ServiceProvider.GetRequiredService<NuDepotContext>();

        // The newest of this year's certificates that wasn't revoked, and can be decrypted.
        List<SigningCertificate> rows = await context.SigningCertificates
            .Where(c => c.Year == now.Year && c.RevokedAt == null)
            .OrderByDescending(c => c.Key)
            .ToListAsync(cancellationToken);

        string fingerprint;
        X509Certificate2 certificate;
        if (previous != null && previous.Year == now.Year && rows.Count > 0 && rows[0].Fingerprint == previous.Fingerprint)
        {
            (fingerprint, certificate) = (previous.Fingerprint, previous.Certificate);
        }
        else
        {
            SigningCertificate? row = null;
            X509Certificate2? loaded = null;
            foreach (SigningCertificate candidate in rows)
            {
                loaded = TryLoad(candidate);
                if (loaded != null)
                {
                    row = candidate;
                    break;
                }
            }

            if (row == null)
            {
                row = await GenerateAsync(context, now.Year, now, cancellationToken);
                loaded = Load(row);
            }

            (fingerprint, certificate) = (row.Fingerprint, loaded!);
            previous?.Certificate.Dispose();
        }

        return new(now.Year, fingerprint, certificate, now);
    }

    private async Task<SigningCertificate> GenerateAsync(NuDepotContext context, int year, DateTimeOffset now, CancellationToken cancellationToken)
    {
        SigningCertificate row = Create(year, now);
        await context.SigningCertificates.ExecuteInsertAsync(row, cancellationToken);
        LogGenerated(row);

        return row;
    }

    private SigningCertificate Create(int year, DateTimeOffset now)
    {
        using RSA key = RSA.Create(KeySize);
        CertificateRequest request = new($"CN=NuDepot Repository Signing {year}", key, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.3", "Code Signing")], critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        // Valid from now, since NuGet won't sign with a certificate that isn't valid yet.
        DateTimeOffset expiresAt = ExpiresAt(year);
        using X509Certificate2 certificate = request.CreateSelfSigned(now.AddMinutes(-5), expiresAt);

        return new()
        {
            Year = year,
            Fingerprint = CertificateFingerprint.Of(certificate),
            Certificate = certificate.RawData,
            ProtectedPrivateKey = Protector.Protect(certificate.Export(X509ContentType.Pkcs12)),
            CreatedAt = now.UtcDateTime,
            ExpiresAt = expiresAt.UtcDateTime,
        };
    }

    private void LogGenerated(SigningCertificate row)
        => Log.Generated(Logger, row.Year, row.Fingerprint, new DateTimeOffset(row.ExpiresAt, TimeSpan.Zero));

    private X509Certificate2 Load(SigningCertificate row)
        => TryLoad(row) ?? throw new InvalidOperationException($"The generated signing certificate {row.Fingerprint} can't be decrypted.");

    private X509Certificate2? TryLoad(SigningCertificate row)
    {
        try
        {
            byte[] pkcs12 = Protector.Unprotect(row.ProtectedPrivateKey);
            return X509CertificateLoader.LoadPkcs12(pkcs12, password: null, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException e)
        {
            Log.NotDecrypted(Logger, e, row.Fingerprint);
            return null;
        }
    }

    /// <summary>
    /// The certificate that signs packages in a year.
    /// </summary>
    /// <param name="Year">The year.</param>
    /// <param name="Fingerprint">The certificate's fingerprint.</param>
    /// <param name="Certificate">The certificate, with its private key.</param>
    /// <param name="CheckedAt">When the database was last checked for a newer certificate.</param>
    private sealed record Signer(int Year, string Fingerprint, X509Certificate2 Certificate, DateTimeOffset CheckedAt);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Revoked the repository signing certificate {Fingerprint}")]
        public static partial void Revoked(ILogger logger, string fingerprint);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Generated the repository signing certificate for {Year}, with the fingerprint {Fingerprint}. It expires at {ExpiresAt:u}. NuGet clients don't trust generated certificates, so they warn about the feed's packages")]
        public static partial void Generated(ILogger logger, int year, string fingerprint, DateTimeOffset expiresAt);

        [LoggerMessage(Level = LogLevel.Warning, Message = "The generated signing certificate {Fingerprint} can't be decrypted, because ASP.NET Core data protection's keys changed. A new certificate replaces it, and packages are re-signed with that when they're downloaded")]
        public static partial void NotDecrypted(ILogger logger, Exception exception, string fingerprint);
    }
}
