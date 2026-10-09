using Haven.Core;
using Microsoft.Data.Sqlite;
namespace Haven.Infrastructure;

public sealed partial class ConversationRepository
{
    // Exact constructor input only, never a metadata or permission grant.
    public bool HasOriginalSqliteFactory(ISqliteConnectionFactory sameFactory) => ReferenceEquals(factory, sameFactory);
    internal static Conversation MapOriginalDatabaseRow(SqliteDataReader reader) =>
        new Conversation(
                reader.Guid("id"), (HavenMode)reader.Int32("mode"), (ConversationKind)reader.Int32("kind"), reader.String("title"),
                reader.NullableGuid("container_id"), reader.NullableGuid("lesson_id"), reader.Boolean("is_pinned"), reader.Boolean("is_temporary"),
                reader.DateTimeOffset("created_at"), reader.DateTimeOffset("updated_at"), reader.Boolean("is_archived"),
                reader.NullableGuid("parent_conversation_id"), reader.NullableString("compacted_at") is { } compacted ? DateTimeOffset.Parse(compacted, System.Globalization.CultureInfo.InvariantCulture) : null,
                reader.NullableGuid("space_id"));
}
