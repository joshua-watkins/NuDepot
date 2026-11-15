using NuDepot.Gallery.Extensions;
using Xunit;

namespace NuDepot.Gallery.Tests;

public class RazorExtensionsFacts
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1k")]
    [InlineData(1500, "1.5k")]
    [InlineData(1234, "1.2k")]
    [InlineData(12_345, "12.3k")]
    [InlineData(999_949, "999.9k")]
    // Rounding up to 1000k makes it 1M.
    [InlineData(999_960, "1M")]
    [InlineData(1_234_567, "1.2M")]
    [InlineData(5_000_000_000, "5G")]
    public void FormatsCountsTheShortWay(long count, string expected)
    {
        Assert.Equal(expected, count.ToMetric());
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "a minute ago")]
    [InlineData(5 * 60, "5 minutes ago")]
    [InlineData(60 * 60, "an hour ago")]
    [InlineData(3 * 60 * 60, "3 hours ago")]
    [InlineData(25 * 60 * 60, "yesterday")]
    [InlineData(3 * 24 * 60 * 60, "3 days ago")]
    [InlineData(40 * 24 * 60 * 60, "a month ago")]
    [InlineData(100 * 24 * 60 * 60, "3 months ago")]
    [InlineData(400 * 24 * 60 * 60, "a year ago")]
    [InlineData(800 * 24 * 60 * 60, "2 years ago")]
    public void SaysHowLongAgoATimeWas(int secondsAgo, string expected)
    {
        DateTimeOffset now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, now.UtcDateTime.AddSeconds(-secondsAgo).ToTimeAgo(now));
    }
}
