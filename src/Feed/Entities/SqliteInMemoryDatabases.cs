using Microsoft.Data.Sqlite;
using System.Collections.Concurrent;

namespace NuDepot.Feed.Entities;

/// <summary>
/// Keeps in-memory SQLite databases, like "Data Source=NuDepot;Mode=Memory;Cache=Shared",
/// alive for the application's lifetime. SQLite deletes an in-memory database when its last
/// connection closes, and Entity Framework closes its connection after each operation.
/// </summary>
internal sealed class SqliteInMemoryDatabases : IDisposable
{
    private ConcurrentDictionary<string, SqliteConnection> Connections { get; } = new();

    public void KeepAlive(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return;

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

        if (builder.Mode != SqliteOpenMode.Memory)
            return;

        Connections.GetOrAdd(connectionString, value =>
        {
            SqliteConnection connection = new(value);
            connection.Open();
            return connection;
        });
    }

    public void Dispose()
    {
        foreach (SqliteConnection connection in Connections.Values)
        {
            connection.Dispose();
        }

        Connections.Clear();
    }
}
