using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NuDepot.Core;
using NuDepot.Core.Hosting;

namespace NuDepot.Gallery;

/// <summary>
/// The website's connection to the NuGet feed it displays. The website only talks
/// to the feed through the NuGet V3 protocol.
/// </summary>
public class FeedConnection
{
    private const string DefaultServiceIndexPath = "v3/index.json";

    private readonly NuDepotFeatures _features;
    private readonly IServer? _server;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly bool _isCoHosted;
    private readonly Lazy<string> _serviceIndexUrl;
    private readonly Lazy<NuGetClientFactory> _clientFactory;

    public FeedConnection(
        IOptions<GalleryOptions> options,
        NuDepotFeatures features,
        IServer? server,
        IHttpContextAccessor? httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(options);

        _features = features ?? throw new ArgumentNullException(nameof(features));
        _server = server;
        _httpContextAccessor = httpContextAccessor;

        var configuredUrl = options.Value.ServiceIndexUrl;

        // The server's listening addresses are only known once the application has started,
        // so resolve them on first use.
        _isCoHosted = string.IsNullOrEmpty(configuredUrl);
        _serviceIndexUrl = new Lazy<string>(() => _isCoHosted ? GetLocalServiceIndexUrl() : configuredUrl!);
        _clientFactory = new Lazy<NuGetClientFactory>(() => new NuGetClientFactory(CreateHttpClient(options.Value.ApiKey), ServiceIndexUrl));
    }

    /// <summary>
    /// Whether people can sign in: the gallery is hosted with a feed whose users sign in with passkeys.
    /// Signed-in users read the feed as themselves, so they can browse a feed that's closed to anonymous users.
    /// </summary>
    public bool SignInAvailable => _isCoHosted && _features.Passkeys;

    /// <summary>
    /// The service index URL the website uses to call the feed.
    /// </summary>
    public string ServiceIndexUrl => _serviceIndexUrl.Value;

    /// <summary>
    /// Creates clients for the feed's resources.
    /// </summary>
    public NuGetClientFactory ClientFactory => _clientFactory.Value;

    /// <summary>
    /// Convert a URL returned by the feed into a URL the browser can use.
    /// </summary>
    /// <param name="url">A URL returned by the feed.</param>
    /// <returns>The URL to show in the page, or null if <paramref name="url"/> is empty.</returns>
    public string? ToPublicUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        // A feed configured explicitly is reached by browsers the same way the website reaches it.
        if (!_isCoHosted) return url;

        var request = _httpContextAccessor?.HttpContext?.Request;
        if (request == null) return url;

        // The co-hosted server generates links using the local address the website called it on.
        // Swap that address for the one the browser used to reach this application.
        var feedRoot = new Uri(ServiceIndexUrl).GetLeftPart(UriPartial.Authority);
        if (!url.StartsWith(feedRoot + "/", StringComparison.OrdinalIgnoreCase)) return url;

        var publicRoot = string.Concat(
            request.Scheme,
            "://",
            request.Host.ToUriComponent(),
            request.PathBase.ToUriComponent());

        return publicRoot + url.Substring(feedRoot.Length);
    }

    private string GetLocalServiceIndexUrl()
    {
        var addresses = _server?.Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses == null || addresses.Count == 0)
        {
            throw new InvalidOperationException(
                "Could not determine the address this application listens on. " +
                $"Set '{GalleryOptions.Section}:{nameof(GalleryOptions.ServiceIndexUrl)}' " +
                "to the feed's service index URL.");
        }

        // Prefer HTTP so the call isn't rejected by a certificate issued for the public host name.
        var address = addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ?? addresses.First();

        // Kestrel reports wildcard bindings such as http://*:5000, http://+:5000 or http://[::]:5000.
        address = address
            .Replace("://*", "://localhost")
            .Replace("://+", "://localhost");

        var builder = new UriBuilder(address);
        if (builder.Host is "0.0.0.0" or "[::]" or "::")
        {
            builder.Host = "localhost";
        }

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

        if (SignInAvailable && _httpContextAccessor != null)
        {
            handler = new ForwardCookiesHandler(_httpContextAccessor) { InnerHandler = handler };
        }

        var client = new HttpClient(handler);

        if (!string.IsNullOrEmpty(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-NuGet-ApiKey", apiKey);
        }

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
            var cookies = httpContextAccessor.HttpContext?.Request.Headers.Cookie;
            if (cookies is { Count: > 0 })
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookies.ToString());
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
