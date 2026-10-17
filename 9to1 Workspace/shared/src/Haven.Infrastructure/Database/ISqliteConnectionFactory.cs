using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Defines the sqlite connection factory contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface ISqliteConnectionFactory
{
    Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken);
}

