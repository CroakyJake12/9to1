using Haven.Core;
using Microsoft.Data.Sqlite;
namespace Haven.Infrastructure;

public sealed partial class ContainerRepository
{
    // Exact constructor input only, never a metadata or permission grant.
    public bool HasOriginalSqliteFactory(ISqliteConnectionFactory sameFactory) => ReferenceEquals(factory, sameFactory);
    internal static ContainerDefinition MapOriginalDatabaseRow(SqliteDataReader reader) => Map(reader);
}
