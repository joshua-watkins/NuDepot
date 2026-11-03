using System;

namespace NuGetFeed.Server.Tests.Support;

/// <summary>
/// A clock that tests move forward. It starts at the real time, since the test's cookies expire in real time.
/// </summary>
internal sealed class TestClock : TimeProvider
{
    private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;

    /// <summary>
    /// How far the clock has been moved forward.
    /// </summary>
    public TimeSpan Advanced { get; private set; }

    public override DateTimeOffset GetUtcNow() => _start + Advanced;

    public void Advance(TimeSpan time) => Advanced += time;
}
