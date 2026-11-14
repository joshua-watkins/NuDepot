using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NuDepot.Feed.Entities;
using NuDepot.Feed.Extensions;
using System.Security.Cryptography;
using Xunit;

namespace NuDepot.Feed.Tests.Services;

public sealed class ExecuteInsertTests : IDisposable
{
    // SQLite deletes an in-memory database when its last connection closes.
    private SqliteConnection KeepAlive { get; }
    private NuDepotDbContext Context { get; }

    public ExecuteInsertTests()
    {
        string connectionString = $"Data Source=ExecuteInsertTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        KeepAlive = new SqliteConnection(connectionString);
        KeepAlive.Open();

        DbContextOptions<NuDepotDbContext> options = new DbContextOptionsBuilder<NuDepotDbContext>()
            .UseSqlite(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        Context = new NuDepotDbContext(options);
        Context
            .RunMigrationsAsync(default)
            .GetAwaiter()
            .GetResult();
    }

    public void Dispose()
    {
        Context.Dispose();
        KeepAlive.Dispose();
    }

    private static User NewUser(string userName)
        => new() { UserName = userName, Handle = RandomNumberGenerator.GetBytes(16), CreatedAt = DateTime.UtcNow };

    [Fact]
    public async Task InsertsAndSetsKeysWithoutTracking()
    {
        User user = NewUser("user");

        Assert.Equal(1, await Context.Users.ExecuteInsertAsync(user, default));

        Assert.NotEqual(0, user.Key);
        Assert.Empty(Context.ChangeTracker.Entries());
        Assert.True(await Context.Users.AnyAsync(u => u.Key == user.Key));
    }

    [Fact]
    public async Task StopsTrackingWhenTheInsertFails()
    {
        await Context.Users.ExecuteInsertAsync(NewUser("user"), default);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => Context.Users.ExecuteInsertAsync(NewUser("user"), default));

        Assert.Empty(Context.ChangeTracker.Entries());
        Assert.Equal(1, await Context.Users.ExecuteInsertAsync(NewUser("other"), default));
    }

    [Fact]
    public async Task RefusesToSaveOtherChanges()
    {
        Context.Users.Add(NewUser("pending"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Context.Users.ExecuteInsertAsync(NewUser("user"), default));
    }
}
