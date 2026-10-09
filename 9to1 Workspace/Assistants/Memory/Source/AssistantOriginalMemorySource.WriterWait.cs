using Haven.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantOriginalMemorySource
{
    private static async Task ConfigureOriginalBoundedWriterWaitAsync(CanonicalSqliteOriginalStoreLease pin,
        Action demandOriginalWrite, CancellationToken token)
    {
        SqliteCommand? command = null; var errors = new List<Exception>();
        try
        {
            pin.InvokeOriginalSource(() =>
            {
                demandOriginalWrite();
                // Installed Microsoft.Data.Sqlite.Core 10.0.9 documents that this governs
                // internal BeginTransaction commands. Zero means infinite, never fail-fast.
                pin.Connection.DefaultTimeout = 1;
                command = pin.Connection.CreateCommand(); return command;
            });
            pin.InvokeOriginalSource(() =>
            {
                demandOriginalWrite(); command!.CommandTimeout = 1;
                // Native SQLite's busy handler is separate from provider retries. Both
                // waits must be finite while the exact Den/Home pins are held.
                command.CommandText = "PRAGMA busy_timeout=1;";
            });
            await pin.ReadOriginalSourceAsync(() => command!.ExecuteNonQueryAsync(token)).ConfigureAwait(false);
            pin.InvokeOriginalSource(demandOriginalWrite);
        }
        catch (Exception failure) { errors.Add(failure); }
        if (command is not null)
            try { await pin.CloseOriginalResourceAsync(command).ConfigureAwait(false); }
            catch (Exception failure) { errors.Add(failure); }
        // Busy, foreign callbacks and cleanup failures remain actual retained failures.
        // The product performs no retry or effect replay and makes no single-attempt claim.
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
    }
}
