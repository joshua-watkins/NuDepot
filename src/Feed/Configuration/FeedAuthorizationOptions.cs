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
