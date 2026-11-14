namespace NuDepot.Feed.Upstream;

/// <summary>
/// The mirrors were asked more often than <see cref="Configuration.MirroringOptions.MaxLookupsPerSecond"/> allows,
/// so the request that needed them is refused with 429 Too Many Requests.
/// </summary>
/// <param name="retryAfter">How long until the mirrors may be asked again.</param>
internal sealed class MirrorsBusyException(TimeSpan retryAfter) : Exception("The mirrors are being asked too often.")
{
    /// <summary>
    /// How long until the mirrors may be asked again.
    /// </summary>
    public TimeSpan RetryAfter { get; } = retryAfter;
}
