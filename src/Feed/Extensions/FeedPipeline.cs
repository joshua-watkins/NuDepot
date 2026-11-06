using Microsoft.Extensions.DependencyInjection;

namespace NuDepot.Feed.Extensions;

/// <summary>
/// What the application uses of the feed: the services it added, with <c>AddPasskeys()</c>, and the request
/// pipeline it built, with <c>UseFeed()</c> and <c>UsePasskeys()</c>.
/// </summary>
public sealed class FeedPipeline
{
    /// <summary>
    /// Whether the application maps the feed's endpoints.
    /// </summary>
    public bool UsesFeed { get; internal set; }

    /// <summary>
    /// Whether the application added the services of users who sign in with passkeys.
    /// </summary>
    public bool AddsPasskeys { get; internal set; }

    /// <summary>
    /// Whether the application lets people sign up and sign in with passkeys, and create API keys.
    /// </summary>
    public bool UsesPasskeys { get; internal set; }

    /// <summary>
    /// The application's pipeline, which <c>AddFeed()</c> and <c>AddPasskeys()</c> share.
    /// </summary>
    internal static FeedPipeline Get(IServiceCollection services)
    {
        if (services.FirstOrDefault(service => service.ServiceType == typeof(FeedPipeline))?.ImplementationInstance is FeedPipeline existing)
            return existing;

        FeedPipeline pipeline = new();
        services.AddSingleton(pipeline);
        return pipeline;
    }
}
