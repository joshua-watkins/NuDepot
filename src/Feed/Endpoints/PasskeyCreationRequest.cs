namespace NuDepot.Feed.Endpoints;

/// <summary>
/// The body of a request to start creating a passkey: for a new account, or for the signed-in user.
/// </summary>
internal class PasskeyCreationRequest
{
    /// <summary>
    /// The new account's user name. Not used when adding a passkey to the signed-in user's account.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// A name for the passkey, like "Laptop" or "Password manager".
    /// </summary>
    public string? PasskeyName { get; set; }
}
