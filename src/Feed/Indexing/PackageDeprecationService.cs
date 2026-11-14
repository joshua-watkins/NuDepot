using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;
using NuDepot.Feed.Authentication;
using NuDepot.Feed.Catalog;
using NuDepot.Feed.Entities;
using NuGet.Versioning;

namespace NuDepot.Feed.Indexing;

/// <inheritdoc />
/// <param name="logger">The logger.</param>
/// <param name="packages">The package database.</param>
/// <param name="catalog">The catalog that records the change.</param>
/// <param name="httpContextAccessor">The request, whose client is logged with what it changes.</param>
public partial class PackageDeprecationService(ILogger<PackageDeprecationService> logger, IPackageDatabase packages, ICatalogService catalog, IHttpContextAccessor httpContextAccessor) : IPackageDeprecationService
{
    private ILogger Logger { get; } = logger;
    private IPackageDatabase Packages { get; } = packages;
    private ICatalogService Catalog { get; } = catalog;
    private IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;

    // Who's changing the feed, for the logs.
    private string Actor => FeedActor.Describe(HttpContextAccessor.HttpContext);

    /// <inheritdoc />
    public async Task<bool> DeprecateAsync(string id, NuGetVersion version, IReadOnlyList<string> reasons, string? message, string? alternatePackageId, string? alternatePackageVersionRange, CancellationToken cancellationToken)
    {
        Log.Deprecating(Logger, id, version);

        string[] reasonArray = [.. reasons];
        string? range = alternatePackageId == null ? null : alternatePackageVersionRange;
        bool updated = await UpdateAsync(id, version, setters => setters
            .SetProperty(p => p.DeprecationReasons, reasonArray)
            .SetProperty(p => p.DeprecationMessage, message)
            .SetProperty(p => p.AlternatePackageId, alternatePackageId)
            .SetProperty(p => p.AlternatePackageVersionRange, range),
            cancellationToken);

        if (updated)
            Log.Deprecated(Logger, Actor, id, version, string.Join(", ", reasonArray));

        return updated;
    }

    /// <inheritdoc />
    public async Task<bool> UndeprecateAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        Log.Undeprecating(Logger, id, version);

        bool updated = await UpdateAsync(id, version, setters => setters
            .SetProperty(p => p.DeprecationReasons, (string[]?)null)
            .SetProperty(p => p.DeprecationMessage, (string?)null)
            .SetProperty(p => p.AlternatePackageId, (string?)null)
            .SetProperty(p => p.AlternatePackageVersionRange, (string?)null),
            cancellationToken);

        if (updated)
            Log.Undeprecated(Logger, Actor, id, version);

        return updated;
    }

    private async Task<bool> UpdateAsync(string id, NuGetVersion version, Action<UpdateSettersBuilder<Package>> setters, CancellationToken cancellationToken)
    {
        if (!await Packages.UpdateAsync(id, version, setters, cancellationToken))
        {
            Log.NotFound(Logger, id, version);
            return false;
        }

        // The catalog records the package as it is now.
        Package package = (await Packages.FindOrNullAsync(id, version, includeUnlisted: true, cancellationToken))!;
        await Catalog.AddPackageDetailsAsync([package], cancellationToken);

        return true;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Deprecating package {PackageId} {PackageVersion}...")]
        public static partial void Deprecating(ILogger logger, string packageId, NuGetVersion packageVersion);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Actor} deprecated package {PackageId} {PackageVersion} as {Reasons}")]
        public static partial void Deprecated(ILogger logger, string actor, string packageId, NuGetVersion packageVersion, string reasons);

        [LoggerMessage(Level = LogLevel.Information, Message = "Undeprecating package {PackageId} {PackageVersion}...")]
        public static partial void Undeprecating(ILogger logger, string packageId, NuGetVersion packageVersion);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Actor} undeprecated package {PackageId} {PackageVersion}")]
        public static partial void Undeprecated(ILogger logger, string actor, string packageId, NuGetVersion packageVersion);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not find package {PackageId} {PackageVersion}")]
        public static partial void NotFound(ILogger logger, string packageId, NuGetVersion packageVersion);
    }
}
