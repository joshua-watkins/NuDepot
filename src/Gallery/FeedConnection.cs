using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NuDepot.Core;
using NuDepot.Core.Hosting;
using NuDepot.Core.RepositorySignatures;
using NuDepot.Gallery.Serialization;
using System.Net;
using System.Net.Http.Json;

namespace NuDepot.Gallery;

/// <summary>
/// The website's connection to the NuGet feed it displays. The website only talks
/// to the feed through the NuGet V3 protocol.
/// </summary>
public class FeedConnection
{
    private const string DefaultServiceIndexPath = "v3/index.json";

    // Relative to the service index, like "v3/index.json".
    private const string CoHostedRepositorySignaturesPath = "repository-signatures/5.0.0/index.json";

    private NuDepotFeatures Features { get; }
    private IServer? Server { get; }
    private IHttpContextAccessor? HttpContextAccessor { get; }
    private bool IsCoHosted { get; }
    private Lazy<string> LazyServiceIndexUrl { get; }
    private Lazy<HttpClient> LazyHttpClient { get; }
    private Lazy<NuGetClientFactory> LazyClientFactory { get; }

    /// <summary>
    /// Connects to the feed at <see cref="GalleryOptions.ServiceIndexUrl"/>, or to the feed hosted in the same application if it's unset.
    /// </summary>
    /// <param name="options">The gallery's options.</param>
    /// <param name="features">What the application's NuDepot parts offer.</param>
    /// <param name="server">The application's server, whose addresses reach a feed hosted with it.</param>
    /// <param name="httpContextAccessor">The current request, whose cookies are forwarded to a feed hosted with it.</param>
    public FeedConnection(IOptions<GalleryOptions> options, NuDepotFeatures features, IServer? server, IHttpContextAccessor? httpContextAccessor)
    {
        Features = features;
        Server = server;
        HttpContextAccessor = httpContextAccessor;

        string? configuredUrl = options.Value.ServiceIndexUrl;

        // The server's listening addresses are only known once the application has started,
        // so resolve them on first use.
        IsCoHosted = string.IsNullOrEmpty(configuredUrl);
        LazyServiceIndexUrl = new(() => IsCoHosted ? GetLocalServiceIndexUrl() : configuredUrl!);
        LazyHttpClient = new(() => CreateHttpClient(options.Value.ApiKey));
        LazyClientFactory = new(() => new(LazyHttpClient.Value, ServiceIndexUrl));
    }

    /// <summary>
    /// Whether people can sign in: the gallery is hosted with a feed whose users sign in with passkeys.
    /// Signed-in users read the feed as themselves, so they can browse a feed that's closed to anonymous users.
    /// </summary>
    public bool SignInAvailable => IsCoHosted && Features.Users;

    /// <summary>
    /// The service index URL the website uses to call the feed.
    /// </summary>
    public string ServiceIndexUrl => LazyServiceIndexUrl.Value;

    /// <summary>
    /// Creates clients for the feed's resources.
    /// </summary>
    public NuGetClientFactory ClientFactory => LazyClientFactory.Value;

    /// <summary>
    /// Creates a client for the certificates the feed signs packages with. The resource is only listed in a
    /// service index read over HTTPS, and a feed hosted in the same application is read over HTTP, so for that
    /// feed, which is NuDepot's, the resource is read from its path.
    /// </summary>
    /// <returns>The client.</returns>
    public IRepositorySignaturesClient CreateRepositorySignaturesClient()
    {
        if (!IsCoHosted)
            return ClientFactory.CreateRepositorySignaturesClient();

        string url = new Uri(new Uri(ServiceIndexUrl), CoHostedRepositorySignaturesPath).AbsoluteUri;
        return new RawRepositorySignaturesClient(LazyHttpClient.Value, url);
    }

    /// <summary>
    /// Asks the feed hosted in the same application who the browser's signed-in user is, and what they may do.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The account, or null if people can't sign in.</returns>
    internal async Task<FeedAccount?> GetAccountOrNullAsync(CancellationToken cancellationToken)
    {
        if (!SignInAvailable)
            return null;

        // The account endpoints are at the root of the feed, next to its v3 folder.
        Uri url = new(new Uri(ServiceIndexUrl), "../api/account");
        using HttpResponseMessage response = await LazyHttpClient.Value.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(WebsiteJsonContext.Default.FeedAccount, cancellationToken);
    }

    /// <summary>
    /// Convert a URL returned by the feed into a URL the browser can use.
    /// </summary>
    /// <param name="url">A URL returned by the feed.</param>
    /// <returns>The URL to show in the page, or null if <paramref name="url"/> is empty.</returns>
    public string? ToPublicUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        // A feed configured explicitly is reached by browsers the same way the website reaches it.
        if (!IsCoHosted)
            return url;

        HttpRequest? request = HttpContextAccessor?.HttpContext?.Request;
        if (request == null)
            return url;

        // The co-hosted server generates links using the local address the website called it on, with the
        // browser's scheme if the website passed it on. Swap that address for the one the browser used to
        // reach this application.
        string feedAuthority = new Uri(ServiceIndexUrl).Authority + "/";
        string? path = null;
        foreach (string scheme in (string[])["http://", "https://"])
        {
            if (url.StartsWith(scheme + feedAuthority, StringComparison.OrdinalIgnoreCase))
                path = url[(scheme.Length + feedAuthority.Length - 1)..];
        }

        if (path == null)
            return url;

        string publicRoot = string.Concat(
            request.Scheme,
            "://",
            request.Host.ToUriComponent(),
            request.PathBase.ToUriComponent());

        return publicRoot + path;
    }

    private string GetLocalServiceIndexUrl()
    {
        ICollection<string>? addresses = Server?.Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses == null || addresses.Count == 0)
        {
            throw new InvalidOperationException(
                "Could not determine the address this application listens on. " +
                $"Set '{GalleryOptions.Section}:{nameof(GalleryOptions.ServiceIndexUrl)}' " +
                "to the feed's service index URL.");
        }

        // Prefer HTTP so the call isn't rejected by a certificate issued for the public host name.
        string address = addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ?? addresses.First();

        // Kestrel reports wildcard bindings such as http://*:5000, http://+:5000 or http://[::]:5000.
        address = address
            .Replace("://*", "://localhost")
            .Replace("://+", "://localhost");

        UriBuilder builder = new(address);
        if (builder.Host is "0.0.0.0" or "[::]" or "::")
            builder.Host = "localhost";

        builder.Path = DefaultServiceIndexPath;

        return builder.Uri.AbsoluteUri;
    }

    private HttpClient CreateHttpClient(string? apiKey)
    {
        HttpMessageHandler handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,

            // The browser's cookies are forwarded instead.
            UseCookies = false,
        };

        if (IsCoHosted && HttpContextAccessor != null)
            handler = new CoHostedFeedHandler(HttpContextAccessor, Features, SignInAvailable) { InnerHandler = handler };

        HttpClient client = new(handler);

        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Add("X-NuGet-ApiKey", apiKey);

        return client;
    }

    /// <summary>
    /// Tells the feed hosted in the same application about the browser's request. The website calls the feed
    /// over HTTP, so it passes on that the browser used HTTPS, which a feed that signs packages requires, with the
    /// application's secret so the feed trusts it. With sign-in, it sends the browser's cookies, including its
    /// sign-in cookie, so the feed answers as it would answer the signed-in user.
    /// </summary>
    private sealed class CoHostedFeedHandler(IHttpContextAccessor httpContextAccessor, NuDepotFeatures features, bool forwardCookies) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpRequest? browserRequest = httpContextAccessor.HttpContext?.Request;
            if (browserRequest?.IsHttps == true && request.RequestUri?.Scheme == Uri.UriSchemeHttp)
            {
                request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", Uri.UriSchemeHttps);
                request.Headers.TryAddWithoutValidation(NuDepotFeatures.CoHostedTokenHeader, features.CoHostedToken);
            }

            StringValues? cookies = browserRequest?.Headers.Cookie;
            if (forwardCookies && cookies is { Count: > 0 })
                request.Headers.TryAddWithoutValidation("Cookie", cookies.ToString());

            return base.SendAsync(request, cancellationToken);
        }
    }
}
