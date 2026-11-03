namespace NuGetFeed.Pages;

/// <summary>
/// The pages' shared layout: the header with the navigation and search box.
/// </summary>
/// <param name="Title">The page's title.</param>
/// <param name="PathBase">The application's path base, which links start with.</param>
/// <param name="ActivePage">The navigation link to highlight.</param>
/// <param name="SearchQuery">The search box's value.</param>
public record LayoutModel(string Title, string PathBase, WebsitePage ActivePage = WebsitePage.None, string? SearchQuery = null)
{
    /// <summary>
    /// The URL of one of the website's files, like "images/github-32x32.png".
    /// </summary>
    public string Content(string path) => $"{PathBase}{WebsiteFiles.RequestPath}/{path}";
}

/// <summary>
/// The pages in the website's navigation.
/// </summary>
public enum WebsitePage
{
    None,
    Packages,
    Upload,
    Account,
}

/// <summary>
/// Shown when the website couldn't get the page's content from the feed.
/// </summary>
/// <param name="PathBase">The application's path base, which links start with.</param>
/// <param name="ServiceIndexUrl">The feed the website reads from.</param>
/// <param name="Access">Whether the feed refused the visitor, rather than being unreachable.</param>
/// <param name="SignInUrl">Where the visitor can sign in to read the feed, if they can.</param>
public record FeedErrorModel(string PathBase, string ServiceIndexUrl, FeedAccessError Access = FeedAccessError.None, string? SignInUrl = null);

public enum FeedAccessError
{
    /// <summary>
    /// The feed couldn't be reached, or returned something unexpected.
    /// </summary>
    None,

    /// <summary>
    /// The feed needs the visitor to sign in.
    /// </summary>
    SignInRequired,

    /// <summary>
    /// The visitor is signed in, but their account can't read the feed.
    /// </summary>
    Forbidden,
}

/// <summary>
/// The page where people sign in with a passkey, or create an account.
/// </summary>
/// <param name="PathBase">The application's path base, which links start with.</param>
/// <param name="ReturnUrl">Where to go after signing in.</param>
public record SignInModel(string PathBase, string ReturnUrl);

/// <summary>
/// The signed-in user's account: their passkeys and API keys.
/// </summary>
/// <param name="PathBase">The application's path base, which links start with.</param>
/// <param name="ServiceIndexUrl">The feed's service index, for the nuget.config example.</param>
public record AccountModel(string PathBase, string ServiceIndexUrl);

/// <summary>
/// What the account pages' script needs.
/// </summary>
/// <param name="PathBase">The application's path base, which the account endpoints start with.</param>
/// <param name="ReturnUrl">Where to go after signing in or out.</param>
/// <param name="ServiceIndexUrl">The feed's service index, for the nuget.config example.</param>
public record AccountScriptSettings(string PathBase, string ReturnUrl, string? ServiceIndexUrl = null);
