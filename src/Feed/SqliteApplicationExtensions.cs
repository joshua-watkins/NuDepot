using System;
using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuDepot.Feed.Configuration;
using NuDepot.Feed.Extensions;

namespace NuDepot.Feed;

public static class SqliteApplicationExtensions
{
    public static NuDepotApplication AddSqliteDatabase(this NuDepotApplication app)
    {
        app.Services.AddSingleton<SqliteInMemoryDatabases>();
        app.Services.AddNuDepotDatabaseProvider("Sqlite", (provider, options) =>
        {
            var databaseOptions = provider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>();
            var connectionString = databaseOptions.Value.ConnectionString;

            provider.GetRequiredService<SqliteInMemoryDatabases>().KeepAlive(connectionString);
            options.UseSqlite(connectionString);
        });

        return app;
    }

    public static NuDepotApplication AddSqliteDatabase(
        this NuDepotApplication app,
        Action<DatabaseOptions> configure)
    {
        app.AddSqliteDatabase();
        app.Services.Configure(configure);
        return app;
    }
}

/// <summary>
/// Keeps in-memory SQLite databases, like "Data Source=NuDepot;Mode=Memory;Cache=Shared",
/// alive for the application's lifetime. SQLite deletes an in-memory database when its last
/// connection closes, and Entity Framework closes its connection after each operation.
/// </summary>
internal sealed class SqliteInMemoryDatabases : IDisposable
{
    private readonly ConcurrentDictionary<string, SqliteConnection> _connections = new();

    public void KeepAlive(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return;

        // Leave invalid connection strings for SQLite to report when the context connects.
        SqliteConnectionStringBuilder builder;
        try
        {
            builder = new SqliteConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            return;
        }

        if (builder.Mode != SqliteOpenMode.Memory) return;

        _connections.GetOrAdd(connectionString, value =>
        {
            var connection = new SqliteConnection(value);
            connection.Open();
            return connection;
        });
    }

    public void Dispose()
    {
        foreach (var connection in _connections.Values)
        {
            connection.Dispose();
        }

        _connections.Clear();
    }
}
