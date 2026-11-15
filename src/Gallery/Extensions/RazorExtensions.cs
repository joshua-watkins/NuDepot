using System.Globalization;

namespace NuDepot.Gallery.Extensions;

/// <summary>
/// Formatting for the website's pages, which are in English.
/// </summary>
internal static class RazorExtensions
{
    private static readonly string[] MetricPrefixes = ["", "k", "M", "G", "T", "P", "E"];

    /// <summary>
    /// Formats a count the short way, with at most one decimal, like "999", "1.5k" or "12.3M".
    /// </summary>
    /// <param name="value">The count.</param>
    /// <returns>The formatted count.</returns>
    public static string ToMetric(this long value)
    {
        // A value that would round up to 1000, like 999,960, takes the next prefix instead: "1M", not "1000k".
        double scaled = value;
        int prefix = 0;
        while (Math.Abs(scaled) >= 999.95 && prefix < MetricPrefixes.Length - 1)
        {
            scaled /= 1000;
            prefix++;
        }

        if (prefix == 0)
            return value.ToString(CultureInfo.InvariantCulture);

        return Math.Round(scaled, 1).ToString("0.#", CultureInfo.InvariantCulture) + MetricPrefixes[prefix];
    }

    /// <summary>
    /// Says how long ago a time was, like "just now", "5 minutes ago", "yesterday" or "3 years ago".
    /// </summary>
    /// <param name="time">The time, in UTC.</param>
    /// <param name="now">The current time.</param>
    /// <returns>How long ago it was.</returns>
    public static string ToTimeAgo(this DateTime time, DateTimeOffset now)
    {
        TimeSpan ago = now.UtcDateTime - DateTime.SpecifyKind(time, DateTimeKind.Utc);
        if (ago < TimeSpan.FromMinutes(1))
            return "just now";
        if (ago < TimeSpan.FromHours(1))
            return Ago((int)ago.TotalMinutes, "minute");
        if (ago < TimeSpan.FromDays(1))
            return Ago((int)ago.TotalHours, "hour");
        if (ago < TimeSpan.FromDays(2))
            return "yesterday";
        if (ago < TimeSpan.FromDays(30))
            return Ago((int)ago.TotalDays, "day");
        if (ago < TimeSpan.FromDays(365))
            return Ago((int)(ago.TotalDays / 30), "month");

        return Ago((int)(ago.TotalDays / 365), "year");
    }

    private static string Ago(int count, string unit)
        => count == 1 ? $"a{(unit == "hour" ? "n" : "")} {unit} ago" : $"{count} {unit}s ago";
}
