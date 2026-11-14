namespace NuDepot.Gallery.Slices;

/// <summary>
/// The model of <see cref="_Layout"/>, the slices' shared layout: the header with the navigation and search box.
/// </summary>
/// <param name="Title">The page's title.</param>
/// <param name="PathBase">The application's path base, which links start with.</param>
/// <param name="ActivePage">The navigation link to highlight.</param>
/// <param name="SearchQuery">The search box's value.</param>
internal record LayoutModel(string Title, string PathBase, WebsitePage ActivePage = WebsitePage.None, string? SearchQuery = null)
{
    /// <summary>
    /// The URL of one of the website's files, like "css/site.css".
    /// </summary>
    public string Content(string path) => $"{PathBase}{WebsiteFiles.RequestPath}/{path}";
}
