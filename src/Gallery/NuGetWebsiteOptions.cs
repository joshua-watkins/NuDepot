namespace NuDepot.Gallery;

public class NuGetWebsiteOptions
{
    public const string DefaultSection = "Website";

    /// <summary>
    /// The absolute URL of the NuGet V3 service index the website reads packages from,
    /// for example https://feed.example.com/v3/index.json. Links to the feed are shown as-is.
    ///
    /// If empty, the website assumes it is hosted in the same application as the server.
    /// It then calls the server on this application's own listening address and rewrites
    /// the feed's links to the address the browser used.
    /// </summary>
    public string? ServiceIndexUrl { get; set; }

    /// <summary>
    /// The API key the website reads the feed with, if the feed doesn't let anyone read it.
    /// Use a ReadOnly key: everyone who can open the website sees what the key can read.
    /// </summary>
    public string? ApiKey { get; set; }

    // things like what frameworks pop up in the search box

    // What the base color is?

    // Idk other stuff
}
