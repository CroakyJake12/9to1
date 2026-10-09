using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

internal static partial class KnowledgeSchema
{
    internal static void ConfigureCanonicalTables(SqliteCommand command)
    {
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS knowledge_records(
                id TEXT PRIMARY KEY,
                category INTEGER NOT NULL,
                topic TEXT NOT NULL,
                title TEXT NOT NULL,
                summary TEXT NOT NULL,
                privacy_class INTEGER NOT NULL,
                confidence REAL NOT NULL,
                is_pinned INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                expires_at TEXT NULL,
                learned_because TEXT NOT NULL,
                sources_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_knowledge_records_category ON knowledge_records(category,updated_at);

            CREATE TABLE IF NOT EXISTS knowledge_record_details(
                id TEXT PRIMARY KEY REFERENCES knowledge_records(id) ON DELETE CASCADE,
                freshness INTEGER NOT NULL DEFAULT 0,
                last_confirmed_at TEXT NULL,
                scope TEXT NOT NULL DEFAULT 'global',
                status INTEGER NOT NULL DEFAULT 0,
                origin INTEGER NOT NULL DEFAULT 0,
                user_correction TEXT NULL,
                supersedes_id TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_knowledge_details_status ON knowledge_record_details(status,last_confirmed_at);

            CREATE TABLE IF NOT EXISTS knowledge_rejections(
                fingerprint TEXT PRIMARY KEY,
                record_id TEXT NULL,
                reason TEXT NULL,
                rejected_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS knowledge_banks(
                id TEXT PRIMARY KEY,
                topic TEXT NOT NULL,
                title TEXT NOT NULL,
                scope TEXT NOT NULL,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                storage_policy TEXT NOT NULL DEFAULT 'local',
                sync_policy TEXT NOT NULL DEFAULT 'disabled',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_knowledge_banks_topic_scope
                ON knowledge_banks(topic COLLATE NOCASE,scope COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS ix_knowledge_banks_enabled ON knowledge_banks(is_enabled,updated_at);

            CREATE TABLE IF NOT EXISTS api_bank_records(
                id TEXT PRIMARY KEY,
                application TEXT NOT NULL,
                api_name TEXT NOT NULL,
                version TEXT NOT NULL,
                documentation_url TEXT NOT NULL,
                actions_json TEXT NOT NULL,
                authentication TEXT NOT NULL,
                requires_internet INTEGER NOT NULL,
                requires_credentials INTEGER NOT NULL,
                cost_per_request TEXT NULL,
                alternatives_json TEXT NOT NULL,
                deprecation TEXT NULL,
                last_checked_at TEXT NOT NULL,
                documentation_hash TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_api_bank_name ON api_bank_records(application,api_name);

            CREATE TABLE IF NOT EXISTS api_bank_details(
                id TEXT PRIMARY KEY REFERENCES api_bank_records(id) ON DELETE CASCADE,
                inputs_json TEXT NOT NULL DEFAULT '[]',
                outputs_json TEXT NOT NULL DEFAULT '[]',
                scopes_json TEXT NOT NULL DEFAULT '[]',
                rate_limits TEXT NOT NULL DEFAULT '',
                pricing TEXT NOT NULL DEFAULT '',
                capability_notes TEXT NOT NULL DEFAULT '',
                limitations TEXT NOT NULL DEFAULT '',
                offline_queue_policy TEXT NOT NULL DEFAULT '',
                is_pinned INTEGER NOT NULL DEFAULT 0,
                source_url TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS background_learning_settings(
                id INTEGER PRIMARY KEY CHECK(id=1),
                global_enabled INTEGER NOT NULL DEFAULT 1,
                mode INTEGER NOT NULL DEFAULT 1,
                disabled_categories_json TEXT NOT NULL DEFAULT '[]',
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS background_learning_tasks(
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                category INTEGER NOT NULL,
                priority INTEGER NOT NULL,
                status INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                source TEXT NOT NULL,
                started_at TEXT NULL,
                last_run_at TEXT NULL,
                completed_at TEXT NULL,
                result TEXT NULL,
                error TEXT NULL,
                requires_network INTEGER NOT NULL DEFAULT 0,
                requires_model INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS ix_background_learning_tasks_status
                ON background_learning_tasks(status,created_at DESC);
            """;
    }
    internal static readonly IReadOnlyList<(string Name, string Definition)> CanonicalDetailColumns = Array.AsReadOnly(
        new (string Name, string Definition)[]
        {
            ("knowledge_bank_id", "TEXT NULL REFERENCES knowledge_banks(id) ON DELETE SET NULL"),
            ("last_reinforced_at", "TEXT NULL"),
            ("last_used_at", "TEXT NULL"),
            ("is_user_locked", "INTEGER NOT NULL DEFAULT 0"),
            ("app_id", "TEXT NULL"),
            ("project_id", "TEXT NULL"),
            ("agent_id", "TEXT NULL")
        });
}
