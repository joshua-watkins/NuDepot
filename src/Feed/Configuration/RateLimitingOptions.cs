namespace NuDepot.Feed.Configuration;

/// <summary>
/// How many requests each client may send. A signed-in user, or a user's API key, is one client wherever they send
/// from. Other requests, anonymous or with one of the feed's API keys, are grouped by the client's address: an IPv4
/// address, or an IPv6 /64 network. Behind a reverse proxy, that's the address the proxy forwards, so the proxy must
/// be one of the <see cref="FeedOptions.TrustedProxies"/>, or all its clients count as one. Health checks aren't
/// limited. Refused requests get 429 Too Many Requests, with a Retry-After header.
/// </summary>
public class RateLimitingOptions
{
    /// <summary>
    /// Whether requests are limited. On by default. Turn it off if a reverse proxy limits them instead.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// How many requests a client may send at once. The default, 2000, covers restoring a large solution from
    /// scratch.
    /// </summary>
    public int RequestBurst { get; set; } = 2000;

    /// <summary>
    /// How many requests a second a client may keep sending, after <see cref="RequestBurst"/>. Requests beyond it
    /// wait up to a second for their turn before they're refused.
    /// </summary>
    public int RequestsPerSecond { get; set; } = 200;

    /// <summary>
    /// How many requests a second all anonymous clients may send together, for a feed open to the internet, where
    /// many addresses could send at once. Requests with an API key or a signed-in user don't count. 0, the
    /// default, for no overall limit. Anonymous clients may send ten seconds' worth at once.
    /// </summary>
    public int AnonymousRequestsPerSecond { get; set; }

    /// <summary>
    /// How many keys or credentials that aren't valid an address may send in <see cref="FailedAuthenticationMinutes"/>.
    /// After that, its keys aren't checked, even correct ones, and its requests with keys are refused until the time
    /// is up.
    /// </summary>
    public int FailedAuthenticationLimit { get; set; } = 20;

    /// <summary>
    /// The time <see cref="FailedAuthenticationLimit"/> counts over.
    /// </summary>
    public int FailedAuthenticationMinutes { get; set; } = 15;

    /// <summary>
    /// How many requests to sign up, sign in or add a passkey an address may send in <see cref="AccountRequestMinutes"/>.
    /// </summary>
    public int AccountRequestLimit { get; set; } = 30;

    /// <summary>
    /// The time <see cref="AccountRequestLimit"/> counts over.
    /// </summary>
    public int AccountRequestMinutes { get; set; } = 10;
}
