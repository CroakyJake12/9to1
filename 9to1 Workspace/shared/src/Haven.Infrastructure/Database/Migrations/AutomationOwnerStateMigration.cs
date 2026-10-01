namespace Haven.Infrastructure;

/// <summary>Additive canonical owner/revision metadata. Existing Automation, Run and reusable-task
/// rows and legacy payloads remain unchanged; descriptors confer no execution authority.</summary>
public static class AutomationOwnerStateMigration
{
    public const int Version = 28;
    public const string Sql = """
        ALTER TABLE automations ADD COLUMN revision INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE automations ADD COLUMN owner_binding_json TEXT NULL;
        ALTER TABLE automations ADD COLUMN graph_binding_json TEXT NULL;
        ALTER TABLE automations ADD COLUMN operational_state INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE automations ADD COLUMN archived_at TEXT NULL;
        ALTER TABLE automations ADD COLUMN publication_journal_json TEXT NULL;
        ALTER TABLE reusable_tasks ADD COLUMN revision INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE reusable_tasks ADD COLUMN owner_binding_json TEXT NULL;
        ALTER TABLE reusable_tasks ADD COLUMN graph_binding_json TEXT NULL;
        ALTER TABLE reusable_tasks ADD COLUMN operational_state INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE reusable_tasks ADD COLUMN archived_at TEXT NULL;
        ALTER TABLE reusable_tasks ADD COLUMN publication_journal_json TEXT NULL;
        ALTER TABLE automation_runs ADD COLUMN revision INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE automation_runs ADD COLUMN definition_revision INTEGER NULL;
        ALTER TABLE automation_runs ADD COLUMN pinned_graph_json TEXT NULL;
        ALTER TABLE automation_runs ADD COLUMN admission_snapshot_json TEXT NULL;
        ALTER TABLE automation_runs ADD COLUMN continuation_descriptor_json TEXT NULL;
        ALTER TABLE automations ADD COLUMN definition_metadata_json TEXT NULL;
        ALTER TABLE reusable_tasks ADD COLUMN definition_metadata_json TEXT NULL;
        ALTER TABLE automation_runs ADD COLUMN run_details_json TEXT NULL;
        ALTER TABLE automations ADD COLUMN owner_commit_receipt_json TEXT NULL;
        ALTER TABLE reusable_tasks ADD COLUMN owner_commit_receipt_json TEXT NULL;
    """;
}
