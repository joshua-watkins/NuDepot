using NuDepot.Feed.Authentication;

namespace NuDepot.Feed.Configuration;

/// <summary>
/// The feed's API keys, one for each role. Clients present a key in the X-NuGet-ApiKey header, which is what
/// the push command's -k option sends, or as the password of a package source's credentials in nuget.config,
/// with the role's name as the user name, like "ReadWrite". A key's role adds to what anonymous requests may do.
/// </summary>
public class ApiKeyOptions
{
    /// <summary>
    /// A key that may read the feed. Once it's set, anonymous requests may do nothing: the feed is private.
    /// </summary>
    public string? ReadOnly { get; set; }

    /// <summary>
    /// A key that may read, push and unlist packages. Once it's set, anonymous requests may only read, unless
    /// <see cref="ReadOnly"/> is set too.
    /// </summary>
    public string? ReadWrite { get; set; }

    /// <summary>
    /// A key that may do everything, including permanently deleting packages, replacing packages, deprecating
    /// packages and managing vulnerabilities. Nobody may do those without it, or a user with the
    /// <see cref="FeedRole.Admin"/> role.
    /// </summary>
    public string? Admin { get; set; }
}
