using Humanizer;

namespace NuDepot.Gallery.Extensions;

public static class RazorExtensions
{
    public static string ToMetric(this long value)
    {
        return ((double)value).ToMetric();
    }
}
