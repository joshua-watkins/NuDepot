using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Extensions;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NuDepot.Feed.Signing;

/// <inheritdoc />
public sealed class RepositorySigningService : IRepositorySigningService, IDisposable
{
    private RepositorySigningOptions Options { get; }
    private ILogger<RepositorySigningService> Logger { get; }

    /// <summary>
    /// Loads the signing certificate, if <see cref="Configuration.RepositorySigningOptions.IsEnabled"/>.
    /// </summary>
    /// <param name="options">The feed's options.</param>
    /// <param name="logger">The logger.</param>
    public RepositorySigningService(IOptions<FeedOptions> options, ILogger<RepositorySigningService> logger)
    {
        Options = options?.Value?.RepositorySigning ?? new RepositorySigningOptions();
        Logger = logger;

        if (Options.IsEnabled)
        {
            Certificate = LoadCertificate(Options);
            CertificateFingerprint = Convert.ToHexStringLower(SHA256.HashData(Certificate.RawData));
        }
    }

    /// <inheritdoc />
    public X509Certificate2? Certificate { get; }

    /// <inheritdoc />
    public string? CertificateFingerprint { get; }

    /// <inheritdoc />
    public async Task<RepositorySigningResult> SignAsync(Stream package, string serviceIndexUrl, CancellationToken cancellationToken)
    {
        if (Certificate == null)
            return RepositorySigningResult.Unsigned;

        try
        {
            // A package can only have one repository signature. Keep the existing one, which
            // might be ours if the package was downloaded from this server and pushed again.
            X509Certificate2? existingSigner = await GetRepositorySignerOrNullAsync(package, cancellationToken);
            if (existingSigner != null)
                return new RepositorySigningResult(null, IsOurCertificate(existingSigner));

            if (!Uri.TryCreate(serviceIndexUrl, UriKind.Absolute, out Uri? serviceIndexUri) ||
                serviceIndexUri.Scheme != Uri.UriSchemeHttps)
            {
                Logger.LogWarning(
                    "Packages are only repository signed when pushed over HTTPS. The service index URL is {ServiceIndexUrl}",
                    serviceIndexUrl);

                return RepositorySigningResult.Unsigned;
            }

            return new RepositorySigningResult(await SignAsync(package, serviceIndexUri, cancellationToken), IsRepositorySigned: true);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Logger.LogError(e, "Failed to repository sign the package. It will be stored without this server's signature");

            return RepositorySigningResult.Unsigned;
        }
        finally
        {
            package.Position = 0;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Certificate?.Dispose();

    private async Task<Stream> SignAsync(Stream package, Uri serviceIndexUrl, CancellationToken cancellationToken)
    {
        string inputPath = Path.GetTempFileName();
        string outputPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            package.Position = 0;
            using (FileStream input = File.Create(inputPath))
            {
                await package.CopyToAsync(input, cancellationToken);
            }

            Rfc3161TimestampProvider? timestampProvider = string.IsNullOrEmpty(Options.TimestampServerUrl)
                ? null
                : new Rfc3161TimestampProvider(new Uri(Options.TimestampServerUrl));

            // Disposing the request disposes its certificate, so give it a copy of ours.
            using X509Certificate2 certificate = new(Certificate!);
            using RepositorySignPackageRequest request = new(
                certificate,
                NuGet.Common.HashAlgorithmName.SHA256,
                NuGet.Common.HashAlgorithmName.SHA256,
                serviceIndexUrl,
                packageOwners: null);

            using (SigningOptions signingOptions = SigningOptions.CreateFromFilePaths(
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
            using FileStream signed = File.OpenRead(outputPath);
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
        using PackageArchiveReader reader = new(package, leaveStreamOpen: true);

        if (!await reader.IsSignedAsync(cancellationToken))
            return null;

        PrimarySignature? primary = await reader.GetPrimarySignatureAsync(cancellationToken);
        if (primary == null)
            return null;

        if (primary.Type == SignatureType.Repository)
            return primary.SignerInfo.Certificate;

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

        StoreLocation location = Enum.Parse<StoreLocation>(options.CertificateStoreLocation, ignoreCase: true);
        StoreName name = Enum.Parse<StoreName>(options.CertificateStoreName, ignoreCase: true);

        using X509Store store = new(name, location);
        store.Open(OpenFlags.ReadOnly);

        X509Certificate2Collection matches = store.Certificates.Find(X509FindType.FindByThumbprint, options.CertificateThumbprint ?? string.Empty, validOnly: false);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Could not find the repository signing certificate {options.CertificateThumbprint} in {location}\\{name}.");
        }

        return matches[0];
    }
}
