namespace Haven.Infrastructure;

/// <summary>Additive dedicated journal; existing Task/Conversation rows remain the canonical stores.</summary>
public static class TaskRunColdRecoveryMigration
{
    public const int Version = 29;
    public const string Sql = """
        CREATE TABLE task_run_recovery_journal(
            capsule_id TEXT PRIMARY KEY,
            task_id TEXT NOT NULL UNIQUE REFERENCES task_execution_state(task_id) ON DELETE RESTRICT,
            context_id TEXT NOT NULL,
            execution_id TEXT NOT NULL,
            expected_revision INTEGER NOT NULL,
            capsule_json TEXT NOT NULL,
            authentication_tag BLOB NOT NULL,
            state_authentication_tag BLOB NOT NULL,
            claim_id TEXT NULL,
            claim_state INTEGER NOT NULL DEFAULT 0 CHECK(claim_state IN(0,1,2,3)),
            acknowledged_json TEXT NULL,
            terminal_json TEXT NULL,
            created_at TEXT NOT NULL
        );
        CREATE INDEX ix_task_run_recovery_journal_run ON task_run_recovery_journal(task_id,execution_id);
        """;
}
