using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Extensions;

namespace NuDepot.Feed.Validation;

/// <summary>
/// Checks, when the application starts, that it uses what the feed's options turn on. Otherwise the
/// feed would ignore its API keys or users, and be closed to everyone, or more open than its options say.
/// </summary>
internal sealed class ValidateFeedPipeline(IOptions<FeedOptions> options, FeedPipeline pipeline) : IHostedService
{
    private const string Section = $"{FeedOptions.Section}:{nameof(FeedOptions.Authorization)}";

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
        bool users = options.Authorization.Users.Enabled;

        if (FeedAccess.HasApiKeys(options) && !pipeline.UsesApiKeys)
        {
            failures.Add(
                $"API keys are configured in '{FeedOptions.Section}:{nameof(FeedOptions.ApiKey)}' or " +
                $"'{Section}:{nameof(FeedAuthorizationOptions.ApiKeys)}', but the application doesn't accept them. " +
                "Call UseApiKeys() on app.UseNuDepot(), or remove the keys.");
        }

        if (users && !pipeline.UsesPasskeys)
        {
            failures.Add(
                $"'{Section}:{nameof(FeedAuthorizationOptions.Users)}:{nameof(UserOptions.Enabled)}' is true, but the " +
                "application doesn't use the feed's users. Call UsePasskeys() on app.UseNuDepot(), or remove the setting.");
        }

        // Users create their own API keys.
        if (users && pipeline.UsesPasskeys && !pipeline.UsesApiKeys)
        {
            failures.Add(
                "The application uses the feed's users, who create their own API keys, but doesn't accept API keys. " +
                "Call UseApiKeys() on app.UseNuDepot().");
        }

        return failures;
    }
}
