using Microsoft.Extensions.Diagnostics.HealthChecks;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Upstream;
using NuDepot.Feed.Upstream.Clients;

namespace NuDepot.Feed.Health;

/// <summary>
/// Reports the mirrors that couldn't be reached, which aren't asked until their break ends. It doesn't call the
/// mirrors, so monitors that check the feed often don't load them: a mirror is only found unreachable when a request
/// needs it.
/// </summary>
/// <param name="upstream">The feed's mirrors.</param>
internal sealed class MirrorsHealthCheck(IUpstreamClient upstream) : IHealthCheck
{
    /// <summary>
    /// The check's name.
    /// </summary>
    public const string Name = "mirrors";

    private IUpstreamClient Upstream { get; } = upstream;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (Upstream is not MirrorChain chain || !chain.HasMirrors)
            return Task.FromResult(HealthCheckResult.Healthy("Mirroring is off."));

        IReadOnlyList<MirrorChain.MirrorStatus> mirrors = chain.GetStatus();
        List<MirrorChain.MirrorStatus> unreachable = [.. mirrors.Where(m => m.UnreachableUntil != null)];
        Dictionary<string, object> data = mirrors.ToDictionary(
            m => m.Name,
            m => (object)(m.UnreachableUntil is DateTimeOffset until ? $"Unreachable until {until:u}" : "Asked"));

        if (unreachable.Count == 0)
            return Task.FromResult(HealthCheckResult.Healthy($"{Plural(mirrors.Count, "mirror")}, all asked.", data));

        // A mirror that stops the chain makes packages unavailable, rather than taking them from another mirror.
        bool stops = unreachable.Any(m => m.WhenUnreachable == UnreachableMirrorBehavior.Stop);
        string names = string.Join(", ", unreachable.Select(m => m.Name));
        string description = stops
            ? $"Couldn't reach {names}, so packages the feed doesn't have can't be restored for now."
            : $"Couldn't reach {names}, so {(unreachable.Count == 1 ? "it's" : "they're")} skipped for now.";

        return Task.FromResult(HealthCheckResult.Degraded(description, data: data));
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
