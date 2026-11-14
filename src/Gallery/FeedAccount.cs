namespace NuDepot.Gallery;

/// <summary>
/// The feed's answer to who's signed in: its api/account endpoint.
/// </summary>
/// <param name="SignedIn">Whether someone is signed in.</param>
/// <param name="UserName">The signed-in user's name.</param>
/// <param name="Role">The signed-in user's role.</param>
/// <param name="Permissions">What the signed-in user may do, like "Push".</param>
/// <param name="PackageDeletion">What deleting a package does: "Unlist" or "HardDelete".</param>
internal sealed record FeedAccount(bool SignedIn, string? UserName, string? Role, string[]? Permissions, string? PackageDeletion);
