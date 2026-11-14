namespace NuDepot.Feed.Signing;

/// <summary>
/// The outcome of replacing this year's generated certificate early.
/// </summary>
/// <param name="Fingerprint">The new certificate's fingerprint.</param>
/// <param name="ExpiresAt">When the new certificate expires.</param>
/// <param name="Revoked">The fingerprints of the certificates it replaced.</param>
internal sealed record SigningCertificateRotation(string Fingerprint, DateTimeOffset ExpiresAt, IReadOnlyList<string> Revoked);
