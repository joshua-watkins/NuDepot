namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The body of a request to start signing in.
/// </summary>
internal class SignInRequest
{
    /// <summary>
    /// The name of the account to sign in to. If empty, the browser offers any of the site's passkeys.
    /// </summary>
    public string? UserName { get; set; }
}
