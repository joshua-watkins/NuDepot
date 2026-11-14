using NuDepot.Feed.Extensions;

namespace NuDepot.Feed.Tests.Support;

/// <summary>
/// The feed's time in tests: the start of 2020, so packages' published times are always the same, plus however far
/// the test moved its clock forward.
/// </summary>
/// <param name="clock">The test's clock.</param>
internal sealed class FixedTime(TestClock clock) : SystemTime
{
    private TestClock Clock { get; } = clock;

    public override DateTime UtcNow => new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) + Clock.Advanced;
}
