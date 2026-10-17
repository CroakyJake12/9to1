/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/SqliteDatabase.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ISqliteConnectionFactory, SqliteDatabase, Migration, Migrations. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Represents sqlite database and keeps its related state and behavior together.
/// </summary>
public sealed partial class SqliteDatabase : IAppDatabase, ISqliteConnectionFactory
{
    /// <summary>
    /// Stores connection string locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _connectionString;
    private readonly IAppPaths _originalPaths;

    public SqliteDatabase(IAppPaths paths)
    {
        _originalPaths = paths;
        SqliteProviderBootstrap.EnsureInitialized();
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            ForeignKeys = true
        }.ToString();
    }

    /// <summary>
    /// Performs open asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Performs initialize asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, "CREATE TABLE IF NOT EXISTS schema_migrations(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);", cancellationToken).ConfigureAwait(false);

        var current = await GetCurrentVersionAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        foreach (var migration in Migrations.All.Where(x => x.Version > current).OrderBy(x => x.Version))
        {
            await ExecuteAsync(connection, transaction, migration.Sql, cancellationToken).ConfigureAwait(false);
            await using var record = connection.CreateCommand();
            record.Transaction = transaction;
            record.CommandText = "INSERT INTO schema_migrations(version, applied_at) VALUES($version, $appliedAt);";
            record.Parameters.AddWithValue("$version", migration.Version);
            record.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            await record.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Durable identity creation belongs ONLY to this SAME owning initialization/write
        // transaction. Read-only catalogue/import observation never seeds or resets it.
        await InitializeOriginalStoreIdentityAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves current version async for the current operation.
    /// </summary>
    private static async Task<int> GetCurrentVersionAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Runs execute async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
