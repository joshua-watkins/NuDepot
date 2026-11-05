using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NuDepot.Core;
using NuDepot.Core.Hosting;
using NuDepot.Core.RepositorySignatures;
using System.Net;

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
    public bool SignInAvailable => IsCoHosted && Features.Passkeys;

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

        // The co-hosted server generates links using the local address the website called it on.
        // Swap that address for the one the browser used to reach this application.
        string feedRoot = new Uri(ServiceIndexUrl).GetLeftPart(UriPartial.Authority);
        if (!url.StartsWith(feedRoot + "/", StringComparison.OrdinalIgnoreCase))
            return url;

        string publicRoot = string.Concat(
            request.Scheme,
            "://",
            request.Host.ToUriComponent(),
            request.PathBase.ToUriComponent());

        return publicRoot + url.Substring(feedRoot.Length);
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

        if (SignInAvailable && HttpContextAccessor != null)
            handler = new ForwardCookiesHandler(HttpContextAccessor) { InnerHandler = handler };

        HttpClient client = new(handler);

        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Add("X-NuGet-ApiKey", apiKey);

        return client;
    }

    /// <summary>
    /// Sends the browser's cookies, including its sign-in cookie, with the website's requests to the
    /// feed hosted in the same application, so the feed answers as it would answer the signed-in user.
    /// </summary>
    private sealed class ForwardCookiesHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            StringValues? cookies = httpContextAccessor.HttpContext?.Request.Headers.Cookie;
            if (cookies is { Count: > 0 })
                request.Headers.TryAddWithoutValidation("Cookie", cookies.ToString());

            return base.SendAsync(request, cancellationToken);
        }
    }
}
