using NuDepot.Feed.Entities;

namespace NuDepot.Feed.Users;

/// <summary>
/// The outcome of a passkey ceremony: the user, or why it failed.
/// </summary>
internal record PasskeyResult(User? User, string? Error)
{
    /// <summary>
    /// A ceremony that succeeded.
    /// </summary>
    /// <param name="user">The user, or null if the account was deleted during the ceremony.</param>
    /// <returns>The result.</returns>
    public static PasskeyResult Succeeded(User? user) => new(user, user == null ? "The account no longer exists." : null);

    /// <summary>
    /// A ceremony that failed.
    /// </summary>
    /// <param name="error">Why it failed, to show the user.</param>
    /// <returns>The result.</returns>
    public static PasskeyResult Failed(string error) => new(null, error);
}
