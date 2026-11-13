using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Extensions;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;

namespace NuDepot.Feed.Signing;

/// <summary>
/// Signs packages with the certificate that <paramref name="certificates"/> says signs packages now.
/// </summary>
/// <param name="options">The signing options.</param>
/// <param name="certificates">The configured certificate, or the generated ones.</param>
/// <param name="logger">The logger.</param>
public sealed partial class RepositorySigningService(IOptions<RepositorySigningOptions> options, ISigningCertificateSource certificates, ILogger<RepositorySigningService> logger) : IRepositorySigningService, IDisposable
{
    private RepositorySigningOptions Options { get; } = options.Value;
    private ISigningCertificateSource Certificates { get; } = certificates;
    private ILogger<RepositorySigningService> Logger { get; } = logger;

    // NuGet checks a request's certificate chain the first time it signs with it, which takes seconds, so
    // requests are kept for each certificate and service index URL. A request signs one package at a time.
    private ConcurrentDictionary<(string Fingerprint, Uri ServiceIndexUrl), CachedRequest> Requests { get; } = new();

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (CachedRequest cached in Requests.Values)
        {
            cached.Request.Dispose();
            cached.Lock.Dispose();
        }
    }

    /// <inheritdoc />
    public bool IsEnabled => Options.IsEnabled;

    /// <inheritdoc />
    public async Task<IReadOnlyList<X509Certificate2>> GetCertificatesAsync(CancellationToken cancellationToken)
        => IsEnabled ? await Certificates.GetAdvertisedCertificatesAsync(cancellationToken) : [];

    /// <inheritdoc />
    public async Task<X509Certificate2?> FindCertificateAsync(string fingerprint, CancellationToken cancellationToken)
        => IsEnabled ? await Certificates.FindCertificateAsync(fingerprint, cancellationToken) : null;

    /// <inheritdoc />
    public async Task<string?> GetSigningFingerprintAsync(CancellationToken cancellationToken)
        => IsEnabled ? CertificateFingerprint.Of(await Certificates.GetSigningCertificateAsync(cancellationToken)) : null;

    /// <inheritdoc />
    public async Task<RepositorySigningResult> SignAsync(Stream package, string serviceIndexUrl, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            return RepositorySigningResult.Unsigned;

        try
        {
            // A package can only have one repository signature. Keep the existing one, which might be ours
            // if the package was downloaded from this server and pushed again.
            RepositorySignatureDetails? existing = await GetRepositorySignatureAsync(package, cancellationToken);
            if (existing != null)
            {
                bool ours = await Certificates.FindCertificateAsync(existing.Fingerprint, cancellationToken) != null;
                return new(null, ours, ours ? existing.Fingerprint : null);
            }

            if (!TryGetHttpsUrl(serviceIndexUrl, out Uri? serviceIndexUri))
                return RepositorySigningResult.Unsigned;

            return await SignUnsignedAsync(package, serviceIndexUri, removeRepositorySignatures: false, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.SigningFailed(Logger, e);
            return RepositorySigningResult.Unsigned;
        }
        finally
        {
            package.Position = 0;
        }
    }

    /// <inheritdoc />
    public async Task<RepositorySigningResult> ResignAsync(Stream package, string serviceIndexUrl, CancellationToken cancellationToken)
    {
        if (!IsEnabled || !TryGetHttpsUrl(serviceIndexUrl, out Uri? serviceIndexUri))
            return RepositorySigningResult.Unsigned;

        try
        {
            return await SignUnsignedAsync(package, serviceIndexUri, removeRepositorySignatures: true, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.ResigningFailed(Logger, e);
            return RepositorySigningResult.Unsigned;
        }
        finally
        {
            package.Position = 0;
        }
    }

    /// <inheritdoc />
    public async Task<RepositorySignatureDetails?> GetRepositorySignatureAsync(Stream package, CancellationToken cancellationToken)
    {
        try
        {
            package.Position = 0;
            using PackageArchiveReader reader = new(package, leaveStreamOpen: true);

            if (!await reader.IsSignedAsync(cancellationToken))
                return null;

            PrimarySignature? primary = await reader.GetPrimarySignatureAsync(cancellationToken);
            if (primary == null)
                return null;

            Signature? repository = primary.Type == SignatureType.Repository
                ? primary
                : RepositoryCountersignature.GetRepositoryCountersignature(primary);

            X509Certificate2? signer = repository?.SignerInfo.Certificate;
            if (repository == null || signer == null)
                return null;

            // A timestamp keeps the signature valid after the certificate expires.
            DateTimeOffset? expiresAt = repository.Timestamps.Count > 0 ? null : signer.NotAfter.ToUniversalTime();
            return new(CertificateFingerprint.Of(signer), expiresAt);
        }
        finally
        {
            package.Position = 0;
        }
    }

    private CachedRequest GetRequest(X509Certificate2 signer, Uri serviceIndexUrl)
    {
        return Requests.GetOrAdd((CertificateFingerprint.Of(signer), serviceIndexUrl), key =>
        {
            // Disposing the request disposes its certificate, so give it a copy of ours.
            RepositorySignPackageRequest request = new(new X509Certificate2(signer), Certificates.HashAlgorithm, Certificates.HashAlgorithm, key.ServiceIndexUrl, packageOwners: null)
            {
                AllowUntrustedRoot = Certificates.AllowUntrustedRoot,
            };
            return new(request, new SemaphoreSlim(1, 1));
        });
    }

    private bool TryGetHttpsUrl(string serviceIndexUrl, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? serviceIndexUri)
    {
        if (Uri.TryCreate(serviceIndexUrl, UriKind.Absolute, out serviceIndexUri) && serviceIndexUri.Scheme == Uri.UriSchemeHttps)
            return true;

        Log.NotHttps(Logger, serviceIndexUrl);
        serviceIndexUri = null;
        return false;
    }

    private async Task<RepositorySigningResult> SignUnsignedAsync(Stream package, Uri serviceIndexUrl, bool removeRepositorySignatures, CancellationToken cancellationToken)
    {
        string inputPath = Path.GetTempFileName();
        string unsignedPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string outputPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            package.Position = 0;
            using (FileStream input = File.Create(inputPath))
            {
                await package.CopyToAsync(input, cancellationToken);
            }

            // Removes the repository signature, primary or countersignature, and keeps the author signature.
            if (removeRepositorySignatures)
            {
                using FileStream input = File.OpenRead(inputPath);
                using FileStream unsigned = File.Create(unsignedPath);
                await SignedPackageArchiveUtility.RemoveRepositorySignaturesAsync(input, unsigned, cancellationToken);
            }
            else
            {
                File.Move(inputPath, unsignedPath);
            }

            Rfc3161TimestampProvider? timestampProvider = string.IsNullOrEmpty(Options.TimestampServerUrl)
                ? null
                : new Rfc3161TimestampProvider(new Uri(Options.TimestampServerUrl));

            X509Certificate2 signer = await Certificates.GetSigningCertificateAsync(cancellationToken);
            CachedRequest cached = GetRequest(signer, serviceIndexUrl);

            await cached.Lock.WaitAsync(cancellationToken);
            try
            {
                using SigningOptions signingOptions = SigningOptions.CreateFromFilePaths(unsignedPath, outputPath, overwrite: false, new X509SignatureProvider(timestampProvider), NuGet.Common.NullLogger.Instance);

                // Adds a repository countersignature to author signed packages, and a repository
                // primary signature to unsigned ones.
                await SigningUtility.SignAsync(signingOptions, cached.Request, cancellationToken);
            }
            finally
            {
                cached.Lock.Release();
            }

            // Hand the signed package over as a stream that deletes its file when disposed.
            using FileStream signed = File.OpenRead(outputPath);
            return new(await signed.AsTemporaryFileStreamAsync(cancellationToken), IsRepositorySigned: true, CertificateFingerprint.Of(signer));
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(unsignedPath);
            File.Delete(outputPath);
        }
    }

    /// <summary>
    /// A signing request kept for its certificate and service index URL.
    /// </summary>
    /// <param name="Request">The request.</param>
    /// <param name="Lock">Lets the request sign one package at a time.</param>
    private sealed record CachedRequest(RepositorySignPackageRequest Request, SemaphoreSlim Lock);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to repository sign the package. It will be stored without this server's signature")]
        public static partial void SigningFailed(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to re-sign the package. It's served with its existing signature")]
        public static partial void ResigningFailed(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Packages are only repository signed when they're pushed or downloaded over HTTPS. The service index URL is {ServiceIndexUrl}")]
        public static partial void NotHttps(ILogger logger, string serviceIndexUrl);
    }
}
