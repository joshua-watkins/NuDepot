using Humanizer;

namespace NuDepot.Gallery.Extensions;

/// <summary>
/// Formatting for the website's pages.
/// </summary>
public static class RazorExtensions
{
    /// <summary>
    /// Formats a count the short way, like "1.2k".
    /// </summary>
    /// <param name="value">The count.</param>
    /// <returns>The formatted count.</returns>
    public static string ToMetric(this long value)
    {
        return ((double)value).ToMetric();
    }
}
