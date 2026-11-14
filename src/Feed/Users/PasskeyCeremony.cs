namespace NuDepot.Feed.Users;

/// <summary>
/// The kinds of passkey ceremony.
/// </summary>
internal enum PasskeyCeremony
{
    /// <summary>
    /// Creating an account with its first passkey.
    /// </summary>
    SignUp,

    /// <summary>
    /// Adding a passkey to the signed-in user's account.
    /// </summary>
    AddPasskey,

    /// <summary>
    /// Signing in with a passkey.
    /// </summary>
    SignIn,
}
