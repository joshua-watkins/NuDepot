using Microsoft.Extensions.DependencyInjection;

namespace NuDepot.Feed.Extensions;

/// <summary>
/// What the application's request pipeline uses of the feed: its endpoints, with <c>UseFeed()</c>, and the
/// endpoints of its users, with <c>MapUsers()</c>. Whether the application added users is in
/// <see cref="Configuration.UserOptions.IsEnabled"/>.
/// </summary>
internal sealed class FeedPipeline
{
    /// <summary>
    /// Whether the application maps the feed's endpoints.
    /// </summary>
    public bool UsesFeed { get; internal set; }

    /// <summary>
    /// Whether the application maps the endpoints people sign up, sign in and create API keys with.
    /// </summary>
    public bool MapsUsers { get; internal set; }

    /// <summary>
    /// The application's pipeline, which <c>AddFeed()</c> registers.
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
