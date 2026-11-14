using NuDepot.Feed.Authentication;

namespace NuDepot.Feed.Users;

/// <summary>
/// A user, for the administration page.
/// </summary>
/// <param name="Key">The user's key.</param>
/// <param name="UserName">The user's name.</param>
/// <param name="Role">The user's role.</param>
/// <param name="CreatedAt">When the account was created, in UTC.</param>
/// <param name="Passkeys">How many passkeys the user has.</param>
/// <param name="ApiKeys">How many API keys the user has.</param>
/// <param name="Sessions">How many browsers the user is signed in on.</param>
/// <param name="LastSignedInAt">When the user last signed in with a passkey, in UTC.</param>
internal record UserSummary(int Key, string UserName, FeedRole Role, DateTime CreatedAt, int Passkeys, int ApiKeys, int Sessions, DateTime? LastSignedInAt);
