using Microsoft.EntityFrameworkCore;
using NuDepot.Feed.Entities;
using NuGet.Versioning;

namespace NuDepot.Feed;

/// <summary>
/// Stores packages' state in the feed's database.
/// </summary>
/// <param name="context">The package database.</param>
public class PackageDatabase(NuDepotContext context) : IPackageDatabase
{
    private NuDepotContext Context { get; } = context;

    /// <inheritdoc />
    public async Task<PackageAddResult> AddAsync(Package package, CancellationToken cancellationToken)
    {
        try
        {
            Context.Packages.Add(package);

            await Context.SaveChangesAsync(cancellationToken);

            return PackageAddResult.Success;
        }
        catch (DbUpdateException e)
            when (Context.IsUniqueConstraintViolationException(e))
        {
            return PackageAddResult.PackageAlreadyExists;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken)
    {
        return await Context
            .Packages
            .Where(p => p.Id == id)
            .AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return await Context
            .Packages
            .Where(p => p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString())
            .AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Package>> FindAsync(string id, bool includeUnlisted, CancellationToken cancellationToken)
    {
        IQueryable<Package> query = Context.Packages
            .Include(p => p.Dependencies)
            .Include(p => p.PackageTypes)
            .Include(p => p.TargetFrameworks)
            .Where(p => p.Id == id);

        if (!includeUnlisted)
            query = query.Where(p => p.Listed);

        return (await query.ToListAsync(cancellationToken)).AsReadOnly();
    }

    /// <inheritdoc />
    public Task<Package?> FindOrNullAsync(string id, NuGetVersion version, bool includeUnlisted, CancellationToken cancellationToken)
    {
        IQueryable<Package> query = Context.Packages
            .Include(p => p.Dependencies)
            .Include(p => p.PackageTypes)
            .Include(p => p.TargetFrameworks)
            .Where(p => p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString());

        if (!includeUnlisted)
            query = query.Where(p => p.Listed);

        return query.FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> UnlistPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return TryUpdatePackageAsync(id, version, p => p.Listed = false, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> RelistPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return TryUpdatePackageAsync(id, version, p => p.Listed = true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddDownloadAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        await TryUpdatePackageAsync(id, version, p => p.Downloads += 1, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> HardDeletePackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        Package? package = await Context.Packages
            .Where(p => p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString())
            .Include(p => p.Dependencies)
            .Include(p => p.TargetFrameworks)
            .FirstOrDefaultAsync(cancellationToken);

        if (package == null)
            return false;

        Context.Packages.Remove(package);
        await Context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> AllRepositorySignedAsync(CancellationToken cancellationToken)
    {
        return !await Context.Packages.AnyAsync(p => !p.IsRepositorySigned, cancellationToken);
    }

    private async Task<bool> TryUpdatePackageAsync(string id, NuGetVersion version, Action<Package> action, CancellationToken cancellationToken)
    {
        Package? package = await Context.Packages
            .Where(p => p.Id == id)
            .Where(p => p.NormalizedVersionString == version.ToNormalizedString())
            .FirstOrDefaultAsync();

        if (package != null)
        {
            action(package);
            await Context.SaveChangesAsync(cancellationToken);

            return true;
        }

        return false;
    }
}
