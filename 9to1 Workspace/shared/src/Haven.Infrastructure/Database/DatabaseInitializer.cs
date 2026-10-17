/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/ConversationProductionDatabase.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ConversationProductionDatabase, ConversationProductionSchema. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Represents conversation production database and keeps its related state and behavior together.
/// </summary>
public sealed class ConversationProductionDatabase : IAppDatabase
{
    /// <summary>
    /// Stores database locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SqliteDatabase _database;
    /// <summary>
    /// Stores maintenance locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IDatabaseMaintenance? _maintenance;
    /// <summary>
    /// Stores restore locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IDatabaseRestoreService? _restore;

    // Focused repository tests historically construct this wrapper around an isolated
    // temporary database. Production DI selects the longest constructor and applies the
    // complete restore, backup and integrity gate.
    public ConversationProductionDatabase(SqliteDatabase database)
    {
        _database = database;
    }

    public ConversationProductionDatabase(
        SqliteDatabase database,
        IDatabaseMaintenance maintenance)
    {
        _database = database;
        _maintenance = maintenance;
    }

    public ConversationProductionDatabase(
        SqliteDatabase database,
        IDatabaseMaintenance maintenance,
        IDatabaseRestoreService restore)
    {
        _database = database;
        _maintenance = maintenance;
        _restore = restore;
    }

    /// <summary>
    /// Performs initialize asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // A restore is intentionally applied before SqliteDatabase opens its first
        // connection. The restore service re-verifies the backup, creates an emergency
        // copy of the current data and performs an atomic swap or rollback.
        if (_restore is not null)
            await _restore.ApplyPendingRestoreAsync(cancellationToken).ConfigureAwait(false);

        if (_maintenance is not null)
        {
            // One verified backup protects the complete additive migration sequence.
            await _maintenance.PrepareForMigrationAsync(
                Migrations.All.Max(migration => migration.Version),
                cancellationToken).ConfigureAwait(false);
        }

        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await ConversationProductionSchema.EnsureAsync(_database, cancellationToken).ConfigureAwait(false);

        if (_maintenance is not null)
        {
            var health = await _maintenance.VerifyIntegrityAsync(cancellationToken).ConfigureAwait(false);
            if (!health.IsHealthy)
                throw new InvalidDataException("The 9to1 database failed its post-migration integrity check. Startup was stopped to protect the data.");
        }
    }
}
