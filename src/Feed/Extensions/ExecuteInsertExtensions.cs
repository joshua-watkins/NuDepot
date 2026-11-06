using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace NuDepot.Feed.Extensions;

/// <summary>
/// Inserts that run immediately, like <see cref="EntityFrameworkQueryableExtensions.ExecuteUpdateAsync{TSource}(IQueryable{TSource}, Action{Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder{TSource}}, CancellationToken)"/>
/// and <see cref="EntityFrameworkQueryableExtensions.ExecuteDeleteAsync{TSource}(IQueryable{TSource}, CancellationToken)"/>:
/// nothing is left tracked afterwards, even if the insert fails.
/// </summary>
internal static class ExecuteInsertExtensions
{
    /// <summary>
    /// Inserts an entity, with the entities it owns through its navigations, and sets its generated keys.
    /// Navigations must only lead to new entities: refer to existing rows by their foreign keys.
    /// </summary>
    /// <param name="set">The table to insert into.</param>
    /// <param name="entity">The new entity.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The number of rows inserted.</returns>
    public static Task<int> ExecuteInsertAsync<T>(this DbSet<T> set, T entity, CancellationToken cancellationToken) where T : class => set.ExecuteInsertAsync([entity], cancellationToken);

    /// <summary>
    /// Inserts entities, with the entities they own through their navigations, and sets their generated keys.
    /// Navigations must only lead to new entities: refer to existing rows by their foreign keys.
    /// </summary>
    /// <param name="set">The table to insert into.</param>
    /// <param name="entities">The new entities.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The number of rows inserted.</returns>
    public static async Task<int> ExecuteInsertAsync<T>(this DbSet<T> set, IEnumerable<T> entities, CancellationToken cancellationToken) where T : class
    {
        DbContext context = set
            .GetService<ICurrentDbContext>()
            .Context;

        // Saving would save these too.
        if (context.ChangeTracker.Entries().Any())
            throw new InvalidOperationException("The context is tracking entities, which an insert would save.");

        try
        {
            set.AddRange(entities);
            return await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }
}
