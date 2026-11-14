using NuDepot.Feed.Entities;

namespace NuDepot.Feed.Users;

/// <summary>
/// The user of a valid session, and the session's new token if the cookie should be replaced.
/// </summary>
internal record SessionValidation(User? User, string? NewToken)
{
    /// <summary>
    /// The result for a request without a valid session.
    /// </summary>
    public static readonly SessionValidation SignedOut = new(null, null);
}
