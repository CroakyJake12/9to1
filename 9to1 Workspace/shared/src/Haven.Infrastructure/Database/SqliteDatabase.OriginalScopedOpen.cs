using Haven.Application;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class SqliteDatabase
{
    // Exact original constructor input only; not an ownership or content grant.
    public bool HasOriginalPaths(IAppPaths samePaths) => ReferenceEquals(_originalPaths, samePaths);

    internal async Task<SqliteConnection> OpenWithinOriginalSourceAsync(
        CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        // Protected READ must never create a missing database or mutate journal mode.
        // A fresh unpooled original avoids observing a stale pooled file after replacement.
        var connection = source.Invoke(() => new SqliteConnection(new SqliteConnectionStringBuilder(_connectionString)
        { Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()));
        SqliteCommand? pragma = null; var errors = new List<Exception>();
        try
        {
            await source.Read(() => connection.OpenAsync(token)).ConfigureAwait(false);
            pragma = source.Invoke(connection.CreateCommand);
            source.Run(() => pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
            await source.Read(() => pragma.ExecuteNonQueryAsync(token)).ConfigureAwait(false);
        }
        catch (Exception cause) { errors.Add(cause); }
        if (pragma is not null)
            try { await source.CloseAsync(pragma).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count == 0) return connection;
        try { await source.CloseAsync(connection).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        throw new AggregateException("The actual scoped SQLite open/cleanup failed.", errors);
    }
}
