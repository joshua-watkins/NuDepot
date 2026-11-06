using System.Security.Cryptography;

namespace NuDepot.Core.Hosting;

/// <summary>
/// What the application's NuDepot parts offer each other. The parts set these as the application's
/// request pipeline is built, so read them once the application handles requests.
/// </summary>
public sealed class NuDepotFeatures
{
    /// <summary>
    /// The header the gallery sends <see cref="CoHostedToken"/> in.
    /// </summary>
    public const string CoHostedTokenHeader = "X-NuDepot-CoHosted";

    /// <summary>
    /// Whether people can sign in with passkeys: the application uses the feed's users. The gallery then shows
    /// who's signed in, lets people sign in, and lets them create API keys for NuGet clients.
    /// </summary>
    public bool Passkeys { get; set; }

    /// <summary>
    /// A secret of this application's, which the gallery sends with its requests to the feed hosted in the same
    /// application. The feed then trusts the request's X-Forwarded-Proto, which says whether the browser used
    /// HTTPS, even if it doesn't trust forwarded headers from other clients.
    /// </summary>
    public string CoHostedToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
