using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NuDepot.Feed.Entities;
using Xunit;

namespace NuDepot.Feed.Tests;

/// <summary>
/// Databases made by earlier versions are upgraded with their packages.
/// </summary>
public sealed class MigrationTests : IAsyncLifetime
{
    // The migration before dependencies needed a package.
    private const string BeforeRequiredDependencies = "20261112070313_AddMirroredPackagesAndSessionTokenUse";

    private SqliteConnection Connection { get; } = new($"Data Source=Migrations-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

    private NuDepotDbContext Context => field ??= new(new DbContextOptionsBuilder<NuDepotDbContext>()
        .UseSqlite(Connection)
        .Options);

    public async ValueTask InitializeAsync() => await Connection.OpenAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await Connection.DisposeAsync();
    }

    [Fact]
    public async Task DependenciesAreKeptAndDeletedWithTheirPackage()
    {
        IMigrator migrator = Context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeRequiredDependencies, TestContext.Current.CancellationToken);

        long package = await InsertAsync("Packages", new() { ["Id"] = "Contoso.Utils", ["Version"] = "1.0.0" });
        await InsertAsync("PackageDependencies", new() { ["Id"] = "Newtonsoft.Json", ["PackageKey"] = package });
        await InsertAsync("PackageDependencies", new() { ["Id"] = "Orphaned.Dependency" });

        await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);

        // The package's dependency is kept, and the one without a package is gone.
        Assert.Equal(["Newtonsoft.Json"], await QueryAsync("SELECT \"Id\" FROM \"PackageDependencies\""));
        Assert.Equal(["CASCADE"], await QueryAsync("SELECT \"on_delete\" FROM pragma_foreign_key_list('PackageDependencies')"));

        await ExecuteAsync("DELETE FROM \"Packages\"");
        Assert.Empty(await QueryAsync("SELECT \"Id\" FROM \"PackageDependencies\""));
    }

    // Inserts a row with the given values, and a placeholder in each other column that needs one, and returns its key.
    private async Task<long> InsertAsync(string table, Dictionary<string, object> values)
    {
        foreach ((string column, string type) in await RequiredColumnsAsync(table))
        {
            if (!values.ContainsKey(column))
                values[column] = type.StartsWith("TEXT", StringComparison.OrdinalIgnoreCase) ? "x" : type == "BLOB" ? new byte[] { 0 } : 0;
        }

        await using SqliteCommand command = Connection.CreateCommand();
        command.CommandText = $"INSERT INTO \"{table}\" ({string.Join(", ", values.Keys.Select(c => $"\"{c}\""))}) " +
            $"VALUES ({string.Join(", ", values.Keys.Select((_, i) => $"$p{i}"))}); SELECT last_insert_rowid();";
        int index = 0;
        foreach (object value in values.Values)
            command.Parameters.AddWithValue($"$p{index++}", value);

        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    // The columns that need a value: not null, without a default, and not the key.
    private async Task<List<(string Column, string Type)>> RequiredColumnsAsync(string table)
    {
        await using SqliteCommand command = Connection.CreateCommand();
        command.CommandText = $"SELECT \"name\", \"type\" FROM pragma_table_info('{table}') WHERE \"notnull\" = 1 AND \"dflt_value\" IS NULL AND \"pk\" = 0";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        List<(string, string)> columns = [];
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            columns.Add((reader.GetString(0), reader.GetString(1)));

        return columns;
    }

    private async Task<List<string>> QueryAsync(string sql)
    {
        await using SqliteCommand command = Connection.CreateCommand();
        command.CommandText = sql;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        List<string> values = [];
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            values.Add(reader.GetString(0));

        return values;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using SqliteCommand command = Connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
