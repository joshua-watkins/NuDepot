using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Signing;
using System.Security.Cryptography.X509Certificates;

namespace NuDepot.Feed.Health;

/// <summary>
/// Checks that pushed packages can be signed: the signing certificate loads, with its private key, and hasn't
/// expired. A configured certificate that expires soon is degraded, so it's replaced in time. Generated certificates
/// are replaced each year by themselves.
/// </summary>
/// <param name="options">The signing options.</param>
/// <param name="certificates">The configured certificate, or the generated ones.</param>
/// <param name="time">The current time.</param>
internal sealed class SigningHealthCheck(IOptions<RepositorySigningOptions> options, ISigningCertificateSource certificates, TimeProvider time) : IHealthCheck
{
    /// <summary>
    /// The check's name.
    /// </summary>
    public const string Name = "signing";

    /// <summary>
    /// How long before a configured certificate expires that the check warns about it.
    /// </summary>
    internal static readonly TimeSpan ExpiryWarning = TimeSpan.FromDays(30);

    private RepositorySigningOptions Options { get; } = options.Value;
    private ISigningCertificateSource Certificates { get; } = certificates;
    private TimeProvider Time { get; } = time;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!Options.IsEnabled)
            return HealthCheckResult.Healthy("Signing is off.");

        X509Certificate2 certificate;
        try
        {
            certificate = await Certificates.GetSigningCertificateAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "The signing certificate can't be loaded, so pushed packages aren't signed.", e);
        }

        string fingerprint = CertificateFingerprint.Of(certificate);
        DateTimeOffset expiresAt = new(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        Dictionary<string, object> data = new() { ["fingerprint"] = fingerprint, ["expiresAt"] = expiresAt };

        if (!certificate.HasPrivateKey)
            return new HealthCheckResult(context.Registration.FailureStatus, "The signing certificate doesn't have its private key.", data: data);

        DateTimeOffset now = Time.GetUtcNow();
        if (expiresAt <= now)
            return new HealthCheckResult(context.Registration.FailureStatus, $"The signing certificate expired on {expiresAt:yyyy-MM-dd}.", data: data);

        if (Options.HasCertificate && expiresAt - now < ExpiryWarning)
            return HealthCheckResult.Degraded($"The signing certificate expires on {expiresAt:yyyy-MM-dd}. Replace it before then.", data: data);

        return HealthCheckResult.Healthy($"Packages are signed with {fingerprint}, until {expiresAt:yyyy-MM-dd}.", data);
    }
}
