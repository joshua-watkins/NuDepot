namespace NuDepot.Feed.Tests.Support;

/// <summary>
/// A clock that tests set to any time.
/// </summary>
/// <param name="now">The clock's time.</param>
internal sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary>
    /// The clock's time.
    /// </summary>
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
