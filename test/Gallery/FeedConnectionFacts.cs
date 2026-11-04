using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Moq;
using NuDepot.Core;
using Xunit;

namespace NuDepot.Gallery.Tests;

public class FeedConnectionFacts
{
    public static FeedConnection CreateRemoteFeed(string serviceIndexUrl = "https://feed.test/v3/index.json")
    {
        return new FeedConnection(
            Options.Create(new NuGetWebsiteOptions { ServiceIndexUrl = serviceIndexUrl }),
            Options.Create(new AuthenticationOptions()),
            server: null,
            httpContextAccessor: null);
    }

    [Fact]
    public void RemoteFeedUsesConfiguredUrl()
    {
        var target = CreateRemoteFeed();

        Assert.Equal("https://feed.test/v3/index.json", target.ServiceIndexUrl);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("https://feed.test/v3/package/a/1.0.0/a.1.0.0.nupkg", "https://feed.test/v3/package/a/1.0.0/a.1.0.0.nupkg")]
    public void RemoteFeedKeepsUrls(string? url, string? expected)
    {
        var target = CreateRemoteFeed();

        Assert.Equal(expected, target.ToPublicUrl(url));
    }

    [Theory]
    [InlineData("http://localhost:5000", "http://localhost:5000/v3/index.json")]
    [InlineData("http://*:5000", "http://localhost:5000/v3/index.json")]
    [InlineData("http://+:5000", "http://localhost:5000/v3/index.json")]
    [InlineData("http://0.0.0.0:5000", "http://localhost:5000/v3/index.json")]
    [InlineData("http://[::]:5000", "http://localhost:5000/v3/index.json")]
    public void CoHostedFeedUsesListeningAddress(string address, string expected)
    {
        var target = CreateCoHostedFeed(new[] { address });

        Assert.Equal(expected, target.ServiceIndexUrl);
    }

    [Fact]
    public void CoHostedFeedPrefersHttp()
    {
        var target = CreateCoHostedFeed(new[] { "https://localhost:5001", "http://localhost:5000" });

        Assert.Equal("http://localhost:5000/v3/index.json", target.ServiceIndexUrl);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("http://localhost:5000/v3/package/a/1.0.0/icon", "https://nuget.example.com/feed/v3/package/a/1.0.0/icon")]
    [InlineData("HTTP://LOCALHOST:5000/v3/index.json", "https://nuget.example.com/feed/v3/index.json")]
    [InlineData("http://localhost:50001/v3/index.json", "http://localhost:50001/v3/index.json")]
    [InlineData("https://example.org/icon.png", "https://example.org/icon.png")]
    public void CoHostedFeedRewritesLocalUrls(string? url, string? expected)
    {
        var target = CreateCoHostedFeed(new[] { "http://localhost:5000" });

        Assert.Equal(expected, target.ToPublicUrl(url));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void SignInIsAvailableWhenCoHostedWithUsers(bool coHosted, bool users, bool expected)
    {
        var authentication = new AuthenticationOptions();
        if (users) authentication.AddScheme(NuDepotAuthenticationSchemes.User, scheme => scheme.HandlerType = typeof(Mock<IAuthenticationHandler>));

        var target = coHosted
            ? CreateCoHostedFeed(new[] { "http://localhost:5000" }, authentication)
            : new FeedConnection(
                Options.Create(new NuGetWebsiteOptions { ServiceIndexUrl = "https://feed.test/v3/index.json" }),
                Options.Create(authentication),
                server: null,
                httpContextAccessor: null);

        // A remote feed has its own users: the website can't sign people in to it.
        Assert.Equal(expected, target.SignInAvailable);
    }

    private static FeedConnection CreateCoHostedFeed(IEnumerable<string> addresses, AuthenticationOptions? authentication = null)
    {
        var addressesFeature = new ServerAddressesFeature();
        foreach (var address in addresses)
        {
            addressesFeature.Addresses.Add(address);
        }

        var features = new FeatureCollection();
        features.Set<IServerAddressesFeature>(addressesFeature);

        var server = new Mock<IServer>();
        server.Setup(s => s.Features).Returns(features);

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("nuget.example.com");
        context.Request.PathBase = "/feed";

        return new FeedConnection(
            Options.Create(new NuGetWebsiteOptions()),
            Options.Create(authentication ?? new AuthenticationOptions()),
            server.Object,
            new HttpContextAccessor { HttpContext = context });
    }
}
