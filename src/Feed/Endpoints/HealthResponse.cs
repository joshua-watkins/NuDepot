namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The feed's health.
/// </summary>
internal class HealthResponse
{
    /// <summary>
    /// The worst of the checks' statuses: "Healthy", "Degraded" or "Unhealthy".
    /// </summary>
    public string Status { get; set; } = null!;

    /// <summary>
    /// The checks, like the database's and the storage's.
    /// </summary>
    public HealthCheckResponse[] Checks { get; set; } = [];
}
