namespace Haven.Infrastructure;

/// <summary>
/// Makes the canonical Automation run state durable. A Run is historical execution state and must stay
/// bound to the exact definition revision it executed, with its node execution states, typed payloads,
/// causal ancestry and any wait/approval continuation recoverable after a crash or restart. The
/// original table kept only the summary columns, so a restarted process could not tell which nodes had
/// already committed their side effects.
/// </summary>
public static class AutomationRunCanonicalStateMigration
{
    public const int Version = 29;

    public const string Sql = """
        ALTER TABLE automation_runs ADD COLUMN revision INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE automation_runs ADD COLUMN definition_revision INTEGER NULL;
        ALTER TABLE automation_runs ADD COLUMN pinned_graph_json TEXT NULL;
        ALTER TABLE automation_runs ADD COLUMN admission_snapshot_json TEXT NULL;
        ALTER TABLE automation_runs ADD COLUMN continuation_descriptor_json TEXT NULL;
        ALTER TABLE automation_runs ADD COLUMN details_json TEXT NULL;
        CREATE INDEX ix_automation_runs_resumable ON automation_runs(status, scheduled_for);
    """;
}
