using System.Collections.Generic;
using NuDepot.Feed.Authentication;

namespace NuDepot.Feed.Configuration;

/// <summary>
/// Who may do what on the feed. Clients present an API key in the X-NuGet-ApiKey header, or as the
/// password of HTTP Basic authentication, which is how NuGet sends a package source's credentials
/// from a nuget.config file. The application must add the feed's authentication for these settings,
/// and its authorization policies for the roles (see <see cref="FeedAccessMode"/>).
/// </summary>
public class FeedAuthorizationOptions
{
    /// <summary>
    /// What requests without a valid API key may do. If unset, <see cref="FeedRole.ReadOnly"/>.
    /// </summary>
    public FeedRole? AnonymousRole { get; set; }

    /// <summary>
    /// The API keys that grant access to the feed. <see cref="NuDepotOptions.ApiKey"/>, if set,
    /// is also an <see cref="FeedRole.Admin"/> key.
    /// </summary>
    public List<ApiKeyOptions> ApiKeys { get; set; } = new();

    /// <summary>
    /// People who sign in with passkeys. They only apply if the application adds the feed's users.
    /// </summary>
    public UserOptions Users { get; set; } = new();
}

public class UserOptions
{
    /// <summary>
    /// Whether people can sign in with passkeys. The Windows and Linux hosts add the feed's users
    /// when this is true.
    /// </summary>
    public bool Enabled { get; set; }

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
    public List<string> Origins { get; set; } = new();
}

public class ApiKeyOptions
{
    /// <summary>
    /// Who or what uses the key, like "Build server". It appears in the logs.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The secret clients present.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// What the key may do. Required with the feed's authorization policies. Without them,
    /// any key may do everything, so only <see cref="FeedRole.Admin"/> is allowed.
    /// </summary>
    public FeedRole? Role { get; set; }
}
