namespace Haven.Infrastructure;

/// <summary>Additive schema migration for Planner's structured Assignment, Schedule, and Countdown aggregates.</summary>
public static class PlannerRichEntitiesMigration
{
    public const int Version = 27;

    public const string Sql = """
        CREATE TABLE planner_rich_entities(
            id TEXT PRIMARY KEY,
            entity_type INTEGER NOT NULL CHECK(entity_type IN (1,2,3)),
            schema_version INTEGER NOT NULL CHECK(schema_version > 0),
            name TEXT NOT NULL,
            status INTEGER NULL,
            due_at TEXT NULL,
            revision INTEGER NOT NULL CHECK(revision > 0),
            payload_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            modified_at TEXT NOT NULL,
            deleted_at TEXT NULL
        );
        CREATE INDEX ix_planner_rich_entities_type_due
            ON planner_rich_entities(entity_type, deleted_at, due_at, id);
        CREATE INDEX ix_planner_rich_entities_type_status
            ON planner_rich_entities(entity_type, deleted_at, status, modified_at DESC, id);

        CREATE TABLE planner_automation_event_outbox(
            event_id TEXT PRIMARY KEY,
            entity_type INTEGER NOT NULL CHECK(entity_type IN (1,2,3)),
            entity_id TEXT NOT NULL,
            entity_revision INTEGER NOT NULL CHECK(entity_revision > 0),
            event_type TEXT NOT NULL,
            occurrence_id TEXT NULL,
            payload_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            published_at TEXT NULL,
            attempt_count INTEGER NOT NULL DEFAULT 0 CHECK(attempt_count >= 0),
            last_error TEXT NULL,
            UNIQUE(entity_type, entity_id, entity_revision, event_type, occurrence_id)
        );
        CREATE INDEX ix_planner_automation_outbox_pending
            ON planner_automation_event_outbox(published_at, created_at, event_id);
        """;
}
