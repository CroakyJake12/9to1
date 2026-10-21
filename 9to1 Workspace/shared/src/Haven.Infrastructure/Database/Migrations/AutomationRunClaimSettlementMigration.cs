namespace Haven.Infrastructure;

/// <summary>
/// Adds the uncertain-settlement columns to the continuation-claim store. A claim row is the durable
/// owner of a resume in progress; when the exact persistence operation that should settle the resume
/// (re-suspension or terminal completion) cannot be acknowledged, the coordinator records the settlement
/// ON the retained claim row instead of releasing the claim. Retaining the claim prevents a later sweep
/// from re-executing the resumed action B, and the stored exact prepared run/next-schedule/error let a
/// reconciliation replay ONLY that precise operation without fresh graph execution.
/// </summary>
public static class AutomationRunClaimSettlementMigration
{
    public const int Version = 31;

    public const string Sql = """
        ALTER TABLE automation_run_claims ADD COLUMN settlement_kind INTEGER NULL;
        ALTER TABLE automation_run_claims ADD COLUMN settlement_run_json TEXT NULL;
        ALTER TABLE automation_run_claims ADD COLUMN settlement_next_run_at TEXT NULL;
        ALTER TABLE automation_run_claims ADD COLUMN settlement_error TEXT NULL;
        ALTER TABLE automation_run_claims ADD COLUMN settled_at TEXT NULL;
        """;
}
