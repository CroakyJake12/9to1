namespace Haven.Infrastructure;

/// <summary>
/// Adds the separate, additive, typed atomic continuation-claim store used to safely resume durable
/// Wait/Approval Runs. A claim binds one RunID to one running resume worker before any resumed side
/// effect, so two candidate readers of the same resumable run cannot both execute the same action and
/// only race at a terminal CAS. The claim row carries the exact expected revision/status, the pinned
/// definition revision and the continuation digest as the CAS fence, and it records the original
/// definition lease token. The claim itself is a fence, never permission: live authority is revalidated
/// by the owning admission/action paths on every resumed side effect, and a public DTO or forged SQL
/// token is never a grant. Only the repository mints a claim token.
/// </summary>
public static class AutomationRunClaimStoreMigration
{
    public const int Version = 30;

    public const string Sql = """
        CREATE TABLE automation_run_claims(
            run_id TEXT PRIMARY KEY,
            automation_id TEXT NOT NULL REFERENCES automations(id) ON DELETE CASCADE,
            claim_token TEXT NOT NULL,
            claimed_at TEXT NOT NULL,
            expected_revision INTEGER NOT NULL,
            expected_status INTEGER NOT NULL,
            definition_revision INTEGER NULL,
            continuation_digest TEXT NOT NULL
        );
        CREATE INDEX ix_automation_run_claims_due ON automation_run_claims(automation_id);
        """;
}
