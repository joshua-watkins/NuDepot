using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;

namespace NuDepot.Feed.Health;

/// <summary>
/// Checks that packages can be stored: the storage folder can be written to, and its disk has room.
/// </summary>
/// <param name="options">The feed's options.</param>
internal sealed class StorageHealthCheck(IOptions<FeedOptions> options) : IHealthCheck
{
    /// <summary>
    /// The check's name.
    /// </summary>
    public const string Name = "storage";

    /// <summary>
    /// Below this much free space, storage is degraded: pushes and mirrored packages will soon fail.
    /// </summary>
    internal const long LowFreeSpace = 1024L * 1024 * 1024;

    private FeedOptions Options { get; } = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        string path = Options.Storage.Path!;
        Dictionary<string, object> data = new() { ["path"] = path };

        try
        {
            // Writing a file is what pushes and mirroring do.
            Directory.CreateDirectory(path);
            string probe = System.IO.Path.Combine(path, $".health-{Guid.NewGuid():N}");
            await File.WriteAllBytesAsync(probe, [0], cancellationToken);
            File.Delete(probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, $"Packages can't be written to {path}.", e, data);
        }

        long freeSpace = new DriveInfo(System.IO.Path.GetFullPath(path)).AvailableFreeSpace;
        data["freeBytes"] = freeSpace;

        return freeSpace < LowFreeSpace
            ? HealthCheckResult.Degraded($"Only {freeSpace / (1024 * 1024)} MB are free for packages, in {path}.", data: data)
            : HealthCheckResult.Healthy($"Packages are stored in {path}, with {freeSpace / (1024 * 1024 * 1024)} GB free.", data);
    }
}
