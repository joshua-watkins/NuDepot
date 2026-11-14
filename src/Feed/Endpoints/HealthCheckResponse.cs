namespace NuDepot.Feed.Endpoints;

/// <summary>
/// One of the feed's health checks.
/// </summary>
internal class HealthCheckResponse
{
    /// <summary>
    /// The check's name, like "database".
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// "Healthy", "Degraded" or "Unhealthy".
    /// </summary>
    public string Status { get; set; } = null!;

    /// <summary>
    /// What the check found.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// How long the check took, in milliseconds.
    /// </summary>
    public double DurationMilliseconds { get; set; }
}
