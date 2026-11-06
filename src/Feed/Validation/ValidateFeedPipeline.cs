using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Extensions;

namespace NuDepot.Feed.Validation;

/// <summary>
/// Checks, when the application starts, that it uses what it added: the users of <c>AddPasskeys()</c>, with
/// <c>UsePasskeys()</c>.
/// </summary>
internal sealed class ValidateFeedPipeline(FeedPipeline pipeline) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // An application that doesn't map the feed's endpoints, like a command, doesn't serve it.
        if (!pipeline.UsesFeed)
            return Task.CompletedTask;

        List<string> failures = Validate(pipeline);
        if (failures.Count > 0)
            throw new OptionsValidationException(Options.DefaultName, typeof(FeedOptions), failures);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static List<string> Validate(FeedPipeline pipeline)
    {
        List<string> failures = [];

        if (pipeline.AddsPasskeys && !pipeline.UsesPasskeys)
            failures.Add("The application adds the feed's users with AddPasskeys(), but doesn't use them. Call UsePasskeys() on app.UseNuDepot().");

        return failures;
    }
}
