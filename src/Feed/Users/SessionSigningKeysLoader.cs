using Microsoft.Extensions.Hosting;

namespace NuDepot.Feed.Users;

/// <summary>
/// Loads the keys that sign sign-in cookies when the application starts.
/// </summary>
internal sealed class SessionSigningKeysLoader(SessionSigningKeys keys) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => keys.LoadAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
