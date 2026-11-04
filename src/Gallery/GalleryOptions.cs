namespace NuDepot.Gallery;

/// <summary>
/// The gallery's options, bound from the <see cref="Section"/> configuration section.
/// </summary>
public class GalleryOptions
{
    /// <summary>
    /// The configuration section the gallery's options are bound from.
    /// </summary>
    public const string Section = "Gallery";

    /// <summary>
    /// The absolute URL of the NuGet V3 service index the gallery reads packages from,
    /// for example https://feed.example.com/v3/index.json. Links to the feed are shown as-is.
    ///
    /// If empty, the gallery assumes it is hosted in the same application as the feed.
    /// It then calls the feed on this application's own listening address and rewrites
    /// the feed's links to the address the browser used.
    /// </summary>
    public string? ServiceIndexUrl { get; set; }

    /// <summary>
    /// The API key the gallery reads the feed with, if the feed doesn't let anyone read it.
    /// Use a ReadOnly key: everyone who can open the gallery sees what the key can read.
    /// </summary>
    public string? ApiKey { get; set; }

    // things like what frameworks pop up in the search box

    // What the base color is?

    // Idk other stuff
}
