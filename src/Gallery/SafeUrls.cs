namespace NuDepot.Gallery;

/// <summary>
/// Checks URLs that packages provide, like their project's or those in their readme, before the gallery links to
/// them. A package's author controls them, so a "javascript:" URL would run script in the gallery, as whoever clicks
/// it.
/// </summary>
internal static class SafeUrls
{
    private static readonly string[] SafeSchemes = ["http", "https", "mailto"];

    /// <summary>
    /// Whether a link may go to the URL: an HTTP, HTTPS or mailto URL, or a relative one.
    /// </summary>
    public static bool IsSafe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Browsers ignore whitespace and control characters in a URL's scheme, like "java\tscript:".
        string compact = new([.. url.Where(c => c > ' ')]);
        int colon = compact.IndexOf(':');
        int pathStart = compact.IndexOfAny(['/', '?', '#']);

        // Without a scheme, it's relative.
        if (colon < 0 || (pathStart >= 0 && pathStart < colon))
            return true;

        string scheme = compact[..colon];
        return SafeSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An absolute HTTP or HTTPS URL to link to, or null if the URL isn't one.
    /// </summary>
    public static string? AbsoluteOrNull(string? url)
    {
        if (url == null || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri))
            return null;

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;
    }
}
