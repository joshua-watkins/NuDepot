using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using System.Net;

namespace NuDepot.Feed;

/// <summary>
/// The ASP.NET Core options the feed needs: NuGet clients call it from anywhere, push large packages,
/// and may reach it through a reverse proxy.
/// </summary>
/// <param name="feedOptions">The feed's options, which name its reverse proxies.</param>
internal sealed class ConfigureFeedOptions(IOptions<FeedOptions> feedOptions)
    : IConfigureOptions<CorsOptions>
    , IConfigureOptions<FormOptions>
    , IConfigureOptions<ForwardedHeadersOptions>
{
    /// <summary>
    /// The networks whose proxies are trusted when <see cref="FeedOptions.TrustedProxies"/> is empty: this machine
    /// and private networks.
    /// </summary>
    internal static IReadOnlyList<string> PrivateNetworks { get; } =
        ["127.0.0.0/8", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "::1/128", "fc00::/7", "fe80::/10"];

    private IOptions<FeedOptions> Feed { get; } = feedOptions;

    public void Configure(CorsOptions options)
    {
        // TODO: Consider disabling this on production builds.
        options.AddDefaultPolicy(builder => builder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
    }

    public void Configure(FormOptions options)
    {
        options.MultipartBodyLengthLimit = int.MaxValue;
    }

    public void Configure(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // The reverse proxy may be on another machine, but clients elsewhere can't pretend to be one.
        // FeedOptions.UseForwardedHeaders turns them off.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        List<string> proxies = Feed.Value.TrustedProxies;
        foreach (string proxy in proxies.Count > 0 ? proxies : PrivateNetworks)
        {
            if (System.Net.IPNetwork.TryParse(proxy, out System.Net.IPNetwork network))
                options.KnownIPNetworks.Add(network);
            else if (IPAddress.TryParse(proxy, out IPAddress? address))
                options.KnownProxies.Add(address);
        }
    }

    /// <summary>
    /// Whether a <see cref="FeedOptions.TrustedProxies"/> entry is an address or a network.
    /// </summary>
    internal static bool IsValidProxy(string? proxy)
        => System.Net.IPNetwork.TryParse(proxy, out _) || IPAddress.TryParse(proxy, out _);
}
