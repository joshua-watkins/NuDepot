namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The feed's repository signing.
/// </summary>
internal class SigningResponse
{
    /// <summary>
    /// Whether the feed signs pushed packages.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Whether the feed generates its certificates, which can be replaced early.
    /// </summary>
    public bool GeneratesCertificates { get; set; }

    /// <summary>
    /// The feed's certificates: the configured one, or every generated one, newest first.
    /// </summary>
    public SigningCertificateResponse[] Certificates { get; set; } = [];
}
