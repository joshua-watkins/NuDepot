namespace NuDepot.Feed.Extensions;

/// <summary>
/// A wrapper that allows for unit tests related to system time.
/// </summary>
internal class SystemTime
{
    /// <summary>
    /// The current time, in UTC.
    /// </summary>
    public virtual DateTime UtcNow => DateTime.UtcNow;
}
