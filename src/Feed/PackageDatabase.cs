using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using NuGet.Versioning;

namespace NuDepot.Feed;

/// <summary>
/// Stores packages' state in the feed's database.
/// </summary>
/// <param name="context">The package database.</param>
internal class PackageDatabase(NuDepotDbContext context) : IPackageDatabase
{
    private NuDepotDbContext Context { get; } = context;

    /// <inheritdoc />
    public async Task<PackageAddResult> AddAsync(Package package, CancellationToken cancellationToken)
    {
        try
        {
            await Context.Packages.ExecuteInsertAsync(package, cancellationToken);

            return PackageAddResult.Success;
        }
        catch (DbUpdateException e)
            when (NuDepotDbContext.IsUniqueConstraintViolationException(e))
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
    public async Task<bool> HasPushedVersionsAsync(string id, CancellationToken cancellationToken)
    {
        return await Context
            .Packages
            .Where(p => p.Id == id && !p.IsMirrored)
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
        return UpdateAsync(id, version, setters => setters.SetProperty(p => p.Listed, false), cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> RelistPackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        return UpdateAsync(id, version, setters => setters.SetProperty(p => p.Listed, true), cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddDownloadAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        // Counted in the database, so downloads at the same time all count.
        await UpdateAsync(id, version, setters => setters.SetProperty(p => p.Downloads, p => p.Downloads + 1), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> HardDeletePackageAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        int? key = await Find(id, version)
            .Select(p => (int?)p.Key)
            .FirstOrDefaultAsync(cancellationToken);

        if (key == null)
            return false;

        // The database deletes package types and target frameworks with their package, but not dependencies.
        await using IDbContextTransaction transaction = await Context.Database.BeginTransactionAsync(cancellationToken);

        await Context.PackageDependencies
            .Where(d => EF.Property<int?>(d, "PackageKey") == key)
            .ExecuteDeleteAsync(cancellationToken);
        await Context.Packages
            .Where(p => p.Key == key)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> AllRepositorySignedAsync(CancellationToken cancellationToken)
    {
        return !await Context.Packages.AnyAsync(p => !p.IsRepositorySigned, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(string id, NuGetVersion version, Action<UpdateSettersBuilder<Package>> setters, CancellationToken cancellationToken)
    {
        return await Find(id, version).ExecuteUpdateAsync(setters, cancellationToken) > 0;
    }

    private IQueryable<Package> Find(string id, NuGetVersion version)
    {
        string normalizedVersion = version.ToNormalizedString();
        return Context.Packages
            .Where(p => p.Id == id)
            .Where(p => p.NormalizedVersionString == normalizedVersion);
    }
}
