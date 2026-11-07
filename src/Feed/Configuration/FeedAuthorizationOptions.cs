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

    /// <summary>
    /// People who sign in with passkeys, when the application adds them with <c>AddUsers()</c>, and maps their
    /// endpoints with <c>MapUsers()</c>.
    /// </summary>
    public UserOptions Users { get; set; } = new();
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

/// <summary>
/// People who sign in to the gallery with passkeys, and create API keys for NuGet clients, which can't use
/// passkeys. The application lets them with <c>AddUsers()</c> and <c>MapUsers()</c>. With users, requests
/// without a key or a signed-in user may only read the feed.
/// </summary>
public class UserOptions
{
    /// <summary>
    /// Whether the application added the feed's users, with <c>AddUsers()</c>. Configuration can't set it, since
    /// users need their services: with them, requests without a key or a signed-in user may only read the feed.
    /// </summary>
    public bool IsEnabled { get; internal set; }

    /// <summary>
    /// Whether requests without a key or a signed-in user may read the feed. If false, they may do nothing.
    /// </summary>
    public bool AnonymousReads { get; set; } = true;

    /// <summary>
    /// Whether anyone can create an account. The first account is always an
    /// <see cref="FeedRole.Admin"/>, so the feed can be set up.
    /// </summary>
    public bool AllowSignUp { get; set; } = true;

    /// <summary>
    /// The role of accounts after the first.
    /// </summary>
    public FeedRole DefaultRole { get; set; } = FeedRole.ReadOnly;

    /// <summary>
    /// The domain passkeys belong to, like "nuget.example.com". If unset, the host name the
    /// browser used. Passkeys only work for the domain they were created for.
    /// </summary>
    public string? Domain { get; set; }

    /// <summary>
    /// The origins the website is reached at, like "https://nuget.example.com". If unset,
    /// the origin the browser used.
    /// </summary>
    public List<string> Origins { get; set; } = [];
}
