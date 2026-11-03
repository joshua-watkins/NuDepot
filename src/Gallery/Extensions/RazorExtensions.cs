using Humanizer;

namespace NuGet.Gallery.Extensions;

public static class RazorExtensions
{
    public static string ToMetric(this long value)
    {
        return ((double) value).ToMetric();
    }
}
