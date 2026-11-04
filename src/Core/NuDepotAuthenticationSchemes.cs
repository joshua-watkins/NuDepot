namespace NuDepot.Core;

/// <summary>
/// The names of the feed server's authentication schemes, which the website also looks for
/// when it's hosted in the same application.
/// </summary>
public static class NuDepotAuthenticationSchemes
{
    /// <summary>
    /// Authenticates a request by its API key, or by its user's sign-in cookie.
    /// </summary>
    public const string Feed = "NuDepot";

    /// <summary>
    /// Authenticates a request by its API key, in the X-NuGet-ApiKey header or as a Basic password.
    /// </summary>
    public const string ApiKey = "NuDepotApiKey";

    /// <summary>
    /// Authenticates a request by the sign-in cookie of a user who signed in with a passkey.
    /// </summary>
    public const string User = "NuDepotUser";
}
