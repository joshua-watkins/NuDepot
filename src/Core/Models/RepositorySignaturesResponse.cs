using System.Text.Json.Serialization;

namespace NuDepot.Core.Models;

/// <summary>
/// The package source's repository signing capabilities.
///
/// See https://learn.microsoft.com/en-us/nuget/api/repository-signatures-resource
/// </summary>
public class RepositorySignaturesResponse
{
    /// <summary>
    /// Whether all packages on the source are repository signed by one of the
    /// <see cref="SigningCertificates"/>. Must be false on the 4.7.0 and 4.9.0 resources.
    /// </summary>
    [JsonPropertyName("allRepositorySigned")]
    public bool AllRepositorySigned { get; set; }

    /// <summary>
    /// The certificates the source uses to sign packages.
    /// </summary>
    [JsonPropertyName("signingCertificates")]
    public IReadOnlyList<RepositorySigningCertificate> SigningCertificates { get; set; } = null!;
}
