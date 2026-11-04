using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Extensions;
using NuGet.Packaging;
using NuGet.Packaging.Signing;

namespace NuDepot.Feed.Signing;

/// <inheritdoc />
public sealed class RepositorySigningService : IRepositorySigningService, IDisposable
{
    private readonly RepositorySigningOptions _options;
    private readonly ILogger<RepositorySigningService> _logger;

    public RepositorySigningService(IOptions<FeedOptions> options, ILogger<RepositorySigningService> logger)
    {
        _options = options?.Value?.RepositorySigning ?? new RepositorySigningOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_options.IsEnabled)
        {
            Certificate = LoadCertificate(_options);
            CertificateFingerprint = Convert.ToHexStringLower(SHA256.HashData(Certificate.RawData));
        }
    }

    public X509Certificate2? Certificate { get; }

    public string? CertificateFingerprint { get; }

    public async Task<RepositorySigningResult> SignAsync(
        Stream package,
        string serviceIndexUrl,
        CancellationToken cancellationToken)
    {
        if (Certificate == null) return RepositorySigningResult.Unsigned;

        try
        {
            // A package can only have one repository signature. Keep the existing one, which
            // might be ours if the package was downloaded from this server and pushed again.
            var existingSigner = await GetRepositorySignerOrNullAsync(package, cancellationToken);
            if (existingSigner != null)
            {
                return new RepositorySigningResult(null, IsOurCertificate(existingSigner));
            }

            if (!Uri.TryCreate(serviceIndexUrl, UriKind.Absolute, out var serviceIndexUri) ||
                serviceIndexUri.Scheme != Uri.UriSchemeHttps)
            {
                _logger.LogWarning(
                    "Packages are only repository signed when pushed over HTTPS. The service index URL is {ServiceIndexUrl}",
                    serviceIndexUrl);

                return RepositorySigningResult.Unsigned;
            }

            return new RepositorySigningResult(await SignAsync(package, serviceIndexUri, cancellationToken), IsRepositorySigned: true);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogError(e, "Failed to repository sign the package. It will be stored without this server's signature");

            return RepositorySigningResult.Unsigned;
        }
        finally
        {
            package.Position = 0;
        }
    }

    public void Dispose() => Certificate?.Dispose();

    private async Task<Stream> SignAsync(Stream package, Uri serviceIndexUrl, CancellationToken cancellationToken)
    {
        var inputPath = Path.GetTempFileName();
        var outputPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            package.Position = 0;
            using (var input = File.Create(inputPath))
            {
                await package.CopyToAsync(input, cancellationToken);
            }

            var timestampProvider = string.IsNullOrEmpty(_options.TimestampServerUrl)
                ? null
                : new Rfc3161TimestampProvider(new Uri(_options.TimestampServerUrl));

            // Disposing the request disposes its certificate, so give it a copy of ours.
            using var certificate = new X509Certificate2(Certificate!);
            using var request = new RepositorySignPackageRequest(
                certificate,
                NuGet.Common.HashAlgorithmName.SHA256,
                NuGet.Common.HashAlgorithmName.SHA256,
                serviceIndexUrl,
                packageOwners: null);

            using (var signingOptions = SigningOptions.CreateFromFilePaths(
                inputPath,
                outputPath,
                overwrite: false,
                new X509SignatureProvider(timestampProvider),
                NuGet.Common.NullLogger.Instance))
            {
                // Adds a repository countersignature to author signed packages, and a
                // repository primary signature to unsigned ones.
                await SigningUtility.SignAsync(signingOptions, request, cancellationToken);
            }

            // Hand the signed package over as a stream that deletes its file when disposed.
            using var signed = File.OpenRead(outputPath);
            return await signed.AsTemporaryFileStreamAsync(cancellationToken);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    private static async Task<X509Certificate2?> GetRepositorySignerOrNullAsync(Stream package, CancellationToken cancellationToken)
    {
        package.Position = 0;
        using var reader = new PackageArchiveReader(package, leaveStreamOpen: true);

        if (!await reader.IsSignedAsync(cancellationToken)) return null;

        var primary = await reader.GetPrimarySignatureAsync(cancellationToken);
        if (primary == null) return null;

        if (primary.Type == SignatureType.Repository)
        {
            return primary.SignerInfo.Certificate;
        }

        return RepositoryCountersignature.GetRepositoryCountersignature(primary)?.SignerInfo.Certificate;
    }

    private bool IsOurCertificate(X509Certificate2 certificate)
        => string.Equals(
            Convert.ToHexStringLower(SHA256.HashData(certificate.RawData)),
            CertificateFingerprint,
            StringComparison.Ordinal);

    private static X509Certificate2 LoadCertificate(RepositorySigningOptions options)
    {
        if (!string.IsNullOrEmpty(options.CertificatePath))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                options.CertificatePath,
                options.CertificatePassword,
                X509KeyStorageFlags.EphemeralKeySet);
        }

        var location = Enum.Parse<StoreLocation>(options.CertificateStoreLocation, ignoreCase: true);
        var name = Enum.Parse<StoreName>(options.CertificateStoreName, ignoreCase: true);

        using var store = new X509Store(name, location);
        store.Open(OpenFlags.ReadOnly);

        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, options.CertificateThumbprint ?? string.Empty, validOnly: false);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Could not find the repository signing certificate {options.CertificateThumbprint} in {location}\\{name}.");
        }

        return matches[0];
    }
}
