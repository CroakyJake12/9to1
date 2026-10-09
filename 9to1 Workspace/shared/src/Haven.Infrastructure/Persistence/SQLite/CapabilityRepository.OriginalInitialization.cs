using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class CapabilityRepository
{
    // The SAME maintained column/parameter mapping. This explicit setup variant
    // never updates a conflicting ID, re-enables a disabled row or overwrites custom data.
    internal static void BindOriginalMissingBuiltInCommand(SqliteCommand command, CapabilityDefinition actual)
    {
        if (!CapabilityRegistryCatalog.BuiltIns.Any(item => item == actual) || !actual.IsBuiltIn || !actual.IsEnabled)
            throw new UnauthorizedAccessException("Only an exact SAME maintained builtin can be initialized.");
        var boundary = UpsertSql.IndexOf("ON CONFLICT(id)", StringComparison.Ordinal);
        if (boundary < 0) throw new InvalidOperationException("The maintained capability INSERT mapping is unavailable.");
        command.CommandText = UpsertSql[..boundary] + "ON CONFLICT(id) DO NOTHING;";
        Bind(command, actual);
    }
}
