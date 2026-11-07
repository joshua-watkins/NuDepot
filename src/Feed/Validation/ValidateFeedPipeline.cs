using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Extensions;

namespace NuDepot.Feed.Validation;

/// <summary>
/// Checks, when the application starts, that it maps the endpoints of what it added: the users of
/// <c>AddUsers()</c>, with <c>MapUsers()</c>. Otherwise users would be on, so anonymous requests could only read,
/// but nobody could sign up.
/// </summary>
internal sealed class ValidateFeedPipeline(IOptions<FeedOptions> options, FeedPipeline pipeline) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // An application that doesn't map the feed's endpoints, like a command, doesn't serve it.
        if (!pipeline.UsesFeed)
            return Task.CompletedTask;

        List<string> failures = Validate(options.Value, pipeline);
        if (failures.Count > 0)
            throw new OptionsValidationException(Options.DefaultName, typeof(FeedOptions), failures);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static List<string> Validate(FeedOptions options, FeedPipeline pipeline)
    {
        List<string> failures = [];

        if (options.Authorization.Users.IsEnabled && !pipeline.MapsUsers)
            failures.Add("The application adds the feed's users with AddUsers(), but doesn't map their endpoints. Call MapUsers() on app.UseNuDepot().");

        return failures;
    }
}
