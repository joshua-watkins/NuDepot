namespace NuDepot.Core.Hosting;

/// <summary>
/// What the application's NuDepot parts offer each other. The parts set these as the application's
/// request pipeline is built, so read them once the application handles requests.
/// </summary>
public sealed class NuDepotFeatures
{
    /// <summary>
    /// Whether people can sign in with passkeys: the feed's users are enabled, and the application
    /// uses them. The gallery then shows who's signed in, and lets people sign in.
    /// </summary>
    public bool Passkeys { get; set; }
}
