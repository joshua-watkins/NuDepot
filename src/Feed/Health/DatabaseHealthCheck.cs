using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NuDepot.Feed.Entities;

namespace NuDepot.Feed.Health;

/// <summary>
/// Checks that the package database answers.
/// </summary>
/// <param name="context">The package database.</param>
internal sealed class DatabaseHealthCheck(NuDepotDbContext context) : IHealthCheck
{
    /// <summary>
    /// The check's name.
    /// </summary>
    public const string Name = "database";

    private NuDepotDbContext Context { get; } = context;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await Context.Packages.AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy("The database answers.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "The database can't be read.", e);
        }
    }
}
