using NuDepot.Feed.Authentication;

namespace NuDepot.Feed.Configuration;

/// <summary>
/// Who may do what on the feed. Without API keys or users, the feed is open: anyone may read, push and unlist
/// packages (<see cref="FeedRole.ReadWrite"/>). Users, or a <see cref="ApiKeyOptions.ReadWrite"/> key, stop
/// anonymous changes, and a <see cref="ApiKeyOptions.ReadOnly"/> key, or users without
/// <see cref="UserOptions.AnonymousReads"/>, close the feed to anonymous requests.
/// </summary>
public class FeedAuthorizationOptions
{
    /// <summary>
    /// The feed's API keys, one for each role. These aren't the keys users create for themselves.
    /// </summary>
    public ApiKeyOptions ApiKeys { get; set; } = new();
}

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
