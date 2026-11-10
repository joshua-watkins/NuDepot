using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Extensions;
using NuDepot.Feed.Signing;
using System.Security.Cryptography.X509Certificates;

namespace NuDepot.Feed.Validation;

/// <summary>
/// Checks, when the application starts, that the optional features that are on work:
/// <list type="bullet">
/// <item>It maps the endpoints of users, with <c>MapUsers()</c>. Otherwise users would be on, so anonymous requests
/// could only read, but nobody could sign up.</item>
/// <item>The configured signing certificate, if any, loads and has its private key, so a missing file or a wrong
/// password stops the application rather than the first push.</item>
/// </list>
/// </summary>
internal sealed class ValidateFeedPipeline(IOptions<UserOptions> users, IOptions<RepositorySigningOptions> signing, FeedPipeline pipeline, IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // An application that doesn't map the feed's endpoints, like a command, doesn't serve it.
        if (!pipeline.UsesFeed)
            return;

        List<string> failures = Validate(users.Value, pipeline);
        if (signing.Value.IsEnabled && signing.Value.HasCertificate)
            await ValidateCertificateAsync(failures, cancellationToken);

        if (failures.Count > 0)
            throw new OptionsValidationException(Options.DefaultName, typeof(FeedOptions), failures);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static List<string> Validate(UserOptions users, FeedPipeline pipeline)
    {
        List<string> failures = [];

        if (users.IsEnabled && !pipeline.MapsUsers)
            failures.Add("The feed's users are on, but the application doesn't map their endpoints. Call MapUsers() on app.UseNuDepot().");

        return failures;
    }

    private async Task ValidateCertificateAsync(List<string> failures, CancellationToken cancellationToken)
    {
        try
        {
            X509Certificate2 certificate = await services
                .GetRequiredService<ISigningCertificateSource>()
                .GetSigningCertificateAsync(cancellationToken);
            if (!certificate.HasPrivateKey)
                failures.Add($"The repository signing certificate {certificate.Thumbprint} doesn't have its private key, which signing needs.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            failures.Add($"The repository signing certificate couldn't be loaded: {e.Message}");
        }
    }
}
