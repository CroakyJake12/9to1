namespace Haven.Infrastructure;

/// <summary>Adds a compare-and-swap revision without changing existing task identities or checkpoint payloads.</summary>
public static class TaskExecutionRevisionMigration
{
    public const int Version = 28;

    public const string Sql = """
        ALTER TABLE task_execution_state
            ADD COLUMN persistence_revision INTEGER NOT NULL DEFAULT 0 CHECK(persistence_revision >= 0);
        """;
}
