using NuDepot.Feed.Authentication;

namespace NuDepot.Feed.Configuration;

/// <summary>
/// People who sign in to the gallery with passkeys, and create API keys for NuGet clients, which can't use
/// passkeys. With users, requests without a key or a signed-in user may only read the feed. The options are bound
/// from the <see cref="Section"/> configuration section only once the application adds users, with
/// <c>AddUsers()</c> or <c>EnableUsers()</c>, and maps their endpoints with <c>MapUsers()</c>.
/// </summary>
public class UserOptions
{
    /// <summary>
    /// The configuration section the options are bound from.
    /// </summary>
    public const string Section = "Feed:Authorization:Users";

    /// <summary>
    /// Whether people can sign up and sign in. True by default with <c>AddUsers()</c>, and false by default with
    /// <c>EnableUsers()</c>. Either way, the configuration can change it. Without either, it's false, and the
    /// configuration is ignored.
    /// </summary>
    public bool IsEnabled { get; set; }

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
