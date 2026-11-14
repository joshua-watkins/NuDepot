namespace NuDepot.Feed.Signing;

/// <summary>
/// A package's repository signature.
/// </summary>
/// <param name="Fingerprint">The lowercase hex SHA-256 fingerprint of the certificate that made it.</param>
/// <param name="ExpiresAt">When it stops being valid: when its certificate expires. Null if it's timestamped, so it stays valid.</param>
public sealed record RepositorySignatureDetails(string Fingerprint, DateTimeOffset? ExpiresAt);
