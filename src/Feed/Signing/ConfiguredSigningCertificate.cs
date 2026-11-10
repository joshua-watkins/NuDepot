using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using System.Security.Cryptography.X509Certificates;

namespace NuDepot.Feed.Signing;

/// <summary>
/// The certificate at <see cref="RepositorySigningOptions.CertificatePath"/>, or in a certificate store with
/// <see cref="RepositorySigningOptions.CertificateThumbprint"/>. It's loaded once, when the feed first signs a package.
/// </summary>
/// <param name="options">The feed's options.</param>
internal sealed class ConfiguredSigningCertificate(IOptions<RepositorySigningOptions> options) : ISigningCertificateSource, IDisposable
{
    private Lazy<X509Certificate2> Certificate { get; } = new(() => Load(options.Value));

    public NuGet.Common.HashAlgorithmName HashAlgorithm => NuGet.Common.HashAlgorithmName.SHA512;

    public bool AllowUntrustedRoot => false;

    public Task<X509Certificate2> GetSigningCertificateAsync(CancellationToken cancellationToken)
        => Task.FromResult(Certificate.Value);

    public Task<IReadOnlyList<X509Certificate2>> GetAdvertisedCertificatesAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<X509Certificate2>>([Certificate.Value]);

    public Task<X509Certificate2?> FindCertificateAsync(string fingerprint, CancellationToken cancellationToken)
    {
        bool matches = string.Equals(fingerprint, CertificateFingerprint.Of(Certificate.Value), StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(matches ? Certificate.Value : null);
    }

    public void Dispose()
    {
        if (Certificate.IsValueCreated)
            Certificate.Value.Dispose();
    }

    private static X509Certificate2 Load(RepositorySigningOptions options)
    {
        if (!string.IsNullOrEmpty(options.CertificatePath))
            return X509CertificateLoader.LoadPkcs12FromFile(options.CertificatePath, options.CertificatePassword, X509KeyStorageFlags.EphemeralKeySet);

        StoreLocation location = Enum.Parse<StoreLocation>(options.CertificateStoreLocation, ignoreCase: true);
        StoreName name = Enum.Parse<StoreName>(options.CertificateStoreName, ignoreCase: true);

        using X509Store store = new(name, location);
        store.Open(OpenFlags.ReadOnly);

        X509Certificate2Collection matches = store.Certificates.Find(X509FindType.FindByThumbprint, options.CertificateThumbprint ?? string.Empty, validOnly: false);
        if (matches.Count == 0)
            throw new InvalidOperationException($"Could not find the repository signing certificate {options.CertificateThumbprint} in {location}\\{name}.");

        return matches[0];
    }
}
