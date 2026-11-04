using System;
using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace NuDepot.Feed.Entities;

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
