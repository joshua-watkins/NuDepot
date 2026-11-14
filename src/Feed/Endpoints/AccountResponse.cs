namespace NuDepot.Feed.Endpoints;

/// <summary>
/// Who is signed in, and whether they can sign up.
/// </summary>
internal class AccountResponse
{
    /// <summary>
    /// Whether someone is signed in.
    /// </summary>
    public bool SignedIn { get; set; }

    /// <summary>
    /// The signed-in user's name.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// The signed-in user's role.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Whether someone who isn't signed in can create an account.
    /// </summary>
    public bool CanSignUp { get; set; }

    /// <summary>
    /// Whether nobody has an account yet. The first account is an Admin.
    /// </summary>
    public bool IsFirstUser { get; set; }

    /// <summary>
    /// What the signed-in user may do, like "Push" or "Administer": the names of their
    /// <see cref="Authentication.FeedPermissions"/>, which read-only mode limits.
    /// </summary>
    public string[]? Permissions { get; set; }

    /// <summary>
    /// What deleting a package does: "Unlist", or "HardDelete" to remove it permanently. Only for a signed-in user.
    /// </summary>
    public string? PackageDeletion { get; set; }
}
