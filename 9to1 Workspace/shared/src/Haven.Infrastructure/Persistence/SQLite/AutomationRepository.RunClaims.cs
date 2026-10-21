using System.Security.Cryptography;
using System.Text;
using Haven.Application.Automations;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

// Partial companion to AutomationRepository implementing the separate, additive, typed atomic
// continuation-claim store (IAutomationRunClaimStore). The claim is acquired in the SAME SQLite write
// transaction that validates the run's exact revision/status/identity/pin/token/continuation-digest,
// so a losing resume worker returns with zero effects before any resumed side effect.

public sealed partial class AutomationRepository
{
    /// <summary>Reads Runs whose persisted wait/approval is due and are safe to claim. This delegates to
    /// the same truthful resumable read as the continuation half — a candidate read, never an approval
    /// or an execution grant.</summary>
    public async Task<IReadOnlyList<AutomationRun>> GetResumableClaimCandidatesAsync(DateTimeOffset now, int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        return await GetResumableRunsAsync(now, limit, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Atomically binds one running claim to exactly one resumable Run. The transaction re-reads
    /// the authoritative persisted row and verifies the exact expected revision/status/automation
    /// identity/original lease token and continuation digest BEFORE inserting the claim, so neither a
    /// stale view nor caller-supplied JSON ever becomes the fence. Only a genuinely resumable
    /// (Waiting/ApprovalRequired/RetryPending) non-terminal Run may be claimed, and the definition must
    /// still exist, be enabled, AND still hold exactly the Run's original lease token; a lost/stolen/
    /// cleared definition lease revokes the claim before any effect. A conflict on the run_id primary
    /// key makes exactly one concurrent caller win; every loser acquires nothing and performs zero effects.</summary>
    public async Task<AutomationRunClaimAcquisition> TryAcquireRunClaimAsync(AutomationRunClaimRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RunId == Guid.Empty || request.AutomationId == Guid.Empty || string.IsNullOrWhiteSpace(request.LeaseToken)
            || request.ExpectedRevision < 0 || string.IsNullOrWhiteSpace(request.ContinuationDigest))
            return new(false, string.Empty, AutomationResumeErrorKind.WaitResumeFailed, "claim.request.invalid", "The claim request is invalid.");

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        // A short busy timeout lets a concurrent claim winner commit so the loser deterministically sees
        // the existing claim row and loses with zero effects instead of throwing a transient lock error.
        await using (var busy = connection.CreateCommand())
        {
            busy.CommandText = "PRAGMA busy_timeout=5000;";
            await busy.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var existing = await ReadRunRowAsync(connection, transaction, request.RunId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return new(false, string.Empty, AutomationResumeErrorKind.WaitResumeFailed, "run.not.found", "The Run no longer exists and cannot be claimed.");

        // Eligibility fence: only a genuinely resumable non-terminal status may be claimed. Terminal/
        // running/unknown/other states are refused before any effect, even if the caller requested one.
        if (!IsEligibleClaimStatus((AutomationRunStatus)existing.Status))
            return new(false, string.Empty, AutomationResumeErrorKind.WaitResumeFailed, "claim.status.ineligible",
                "The Run is not in a resumable waiting/approval/retry state; refusing to claim.");

        // Due-time fence: a Run must be genuinely due to be claimed. A direct/forged request must not
        // bypass the candidate query and claim a far-future Wait/Approval before its due time.
        if (existing.ScheduledFor > DateTimeOffset.UtcNow)
            return new(false, string.Empty, AutomationResumeErrorKind.WaitResumeFailed, "claim.not.due",
                "The Run is not due yet; refusing to claim before its scheduled due time.");

        if (existing.AutomationId != request.AutomationId)
            return new(false, string.Empty, AutomationResumeErrorKind.WaitResumeFailed, "claim.identity.mismatch", "The claim automation identity does not match the persisted Run.");
        if (existing.LeaseToken != request.LeaseToken)
            return new(false, string.Empty, AutomationResumeErrorKind.WaitResumeFailed, "claim.token.mismatch", "The claim lease token does not match the Run's original token.");
        if (existing.Revision != request.ExpectedRevision)
            return new(false, string.Empty, AutomationResumeErrorKind.WaitResumeFailed, "claim.revision.mismatch", "The claim revision does not match the persisted Run revision.");
        if (existing.Status != (int)request.ExpectedStatus)
            return new(false, string.Empty, AutomationResumeErrorKind.WaitResumeFailed, "claim.status.mismatch", "The claim status does not match the persisted Run status.");
        if (!string.Equals(ComputeContinuationDigest(existing.ContinuationDescriptorJson), request.ContinuationDigest, StringComparison.Ordinal))
            return new(false, string.Empty, AutomationResumeErrorKind.WaitResumeFailed, "claim.continuation.mismatch", "The claim continuation digest does not match the persisted continuation.");

        // The definition must still exist and be operational AND still hold exactly the Run's original
        // lease token. A lost/stolen/cleared definition lease means the run's authority was revoked or
        // superseded; the run stops before any side effect. This is the definition-token equality fence.
        var definition = await ReadDefinitionStateAsync(connection, transaction, request.AutomationId, cancellationToken).ConfigureAwait(false);
        if (definition.Enabled is null)
            return new(false, string.Empty, AutomationResumeErrorKind.NeedsAttention, "definition.deleted", "The definition is gone; the Run cannot be resumed.");
        if (definition.Enabled.Value != true)
            return new(false, string.Empty, AutomationResumeErrorKind.NeedsAttention, "definition.disabled", "The definition is disabled; the Run cannot be resumed.");
        if (existing.LeaseToken is null || string.IsNullOrWhiteSpace(definition.LeaseToken) ||
            !string.Equals(existing.LeaseToken, definition.LeaseToken, StringComparison.Ordinal))
            return new(false, string.Empty, AutomationResumeErrorKind.NeedsAttention, "definition.token.lost",
                "The definition no longer holds this Run's original lease token; the Run cannot be resumed.");

        var claimToken = Guid.NewGuid().ToString("N");
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO automation_run_claims(run_id,automation_id,claim_token,claimed_at,expected_revision,expected_status,definition_revision,continuation_digest)
            VALUES($runId,$automationId,$token,$claimedAt,$expectedRevision,$expectedStatus,$definitionRevision,$digest)
            ON CONFLICT(run_id) DO NOTHING;
            """;
        insert.Parameters.AddWithValue("$runId", request.RunId.ToString());
        insert.Parameters.AddWithValue("$automationId", request.AutomationId.ToString());
        insert.Parameters.AddWithValue("$token", claimToken);
        insert.Parameters.AddWithValue("$claimedAt", DateTimeOffset.UtcNow.ToString("O"));
        insert.Parameters.AddWithValue("$expectedRevision", request.ExpectedRevision);
        insert.Parameters.AddWithValue("$expectedStatus", (int)request.ExpectedStatus);
        insert.Parameters.AddWithValue("$definitionRevision", (object?)existing.DefinitionRevision ?? DBNull.Value);
        insert.Parameters.AddWithValue("$digest", request.ContinuationDigest);

        var inserted = await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return inserted == 1
            ? new(true, claimToken, AutomationResumeErrorKind.None, "claim.acquired", "The Run is claimed for resume.")
            : new(false, string.Empty, AutomationResumeErrorKind.WaitResumeFailed, "claim.lost", "Another worker already claimed this Run; this worker performs no effects.");
    }

    /// <summary>Releases a held claim. Only the owning claim token may release it; an unrelated token is
    /// refused so a losing/stale worker cannot steal another winner's claim. This is also how a settled
    /// (acknowledged) claim — including one that carried an uncertain settlement — is cleared once its
    /// exact persistence operation is durably acknowledged.</summary>
    public async Task ReleaseRunClaimAsync(Guid runId, string claimToken, CancellationToken cancellationToken)
    {
        if (runId == Guid.Empty || string.IsNullOrWhiteSpace(claimToken)) return;
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM automation_run_claims WHERE run_id=$runId AND claim_token=$token;";
        command.Parameters.AddWithValue("$runId", runId.ToString());
        command.Parameters.AddWithValue("$token", claimToken);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Bounded by-ID read of exactly one due resumable Run. The Run is returned only when it is
    /// a genuine due candidate (resumable status and due time); otherwise null. This is the authoritative
    /// read used by the approval path so a large number of other waiting Runs never hides an exact
    /// approved target. It remains a candidate read — never an approval or an execution grant.</summary>
    public async Task<AutomationRun?> GetResumableRunByIdAsync(Guid runId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (runId == Guid.Empty) return null;
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT * FROM automation_runs
            WHERE id=$id
              AND status IN ({(int)AutomationRunStatus.Waiting},{(int)AutomationRunStatus.ApprovalRequired},{(int)AutomationRunStatus.RetryPending})
              AND scheduled_for <= $now
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", runId.ToString());
        command.Parameters.AddWithValue("$now", now.ToUniversalTime().ToString("O"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return ReadRun(reader);
    }

    /// <summary>Records an uncertain settlement on the held claim row, retaining the exact prepared
    /// persistence operation (run JSON and next schedule) and the original error. The claim is retained
    /// so a later sweep cannot re-execute the resumed action B while the exact outcome is unknown. This
    /// is a durable outcome/receipt for the exact persistence operation, never a blanket catch.
    /// The prepared operation is BOUND to the actual held claim: the retained Run's identity, automation,
    /// pinned revision and one-step revision advance must agree with the claim row before the settlement
    /// is recorded, so a payload for another Run cannot be stamped onto this claim.</summary>
    public async Task RecordUncertainSettlementAsync(AutomationRunClaimSettlement settlement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        if (settlement.RunId == Guid.Empty || string.IsNullOrWhiteSpace(settlement.ClaimToken)) return;
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var held = await ReadClaimRowAsync(connection, null, settlement.RunId, settlement.ClaimToken, cancellationToken).ConfigureAwait(false);
        // The claim must exist and the supplied automation identity must match the held claim exactly.
        if (held is null || held.AutomationId != settlement.AutomationId)
            throw new InvalidOperationException("The uncertain settlement does not match the held claim's automation identity.");
        // Suspend/Complete settlements carry the exact prepared Run; bind it to the claim row before
        // recording so a forged/mismatched payload for another Run is refused.
        if (settlement.Kind is AutomationRunClaimSettlementKind.Suspend or AutomationRunClaimSettlementKind.Complete)
        {
            if (string.IsNullOrWhiteSpace(settlement.RunJson))
                throw new InvalidOperationException("A suspension/completion settlement must carry the exact prepared Run.");
            AutomationRun prepared;
            try { prepared = System.Text.Json.JsonSerializer.Deserialize<AutomationRun>(settlement.RunJson)!; }
            catch (System.Text.Json.JsonException) { throw new InvalidOperationException("The prepared settlement Run is unreadable."); }
            if (prepared is null || prepared.Id != settlement.RunId || prepared.AutomationId != settlement.AutomationId)
                throw new InvalidOperationException("The prepared settlement Run identity does not match the claim.");
            // The prepared transition must advance exactly one revision from the claim's expected revision,
            // and must stay bound to the SAME pinned graph revision the claim fenced.
            if (prepared.Revision != held.ExpectedRevision + 1)
                throw new InvalidOperationException("The prepared settlement Run revision does not advance the held claim by exactly one.");
            if (prepared.DefinitionRevision is { } defRev && defRev != held.DefinitionRevision)
                throw new InvalidOperationException("The prepared settlement Run is not bound to the claim's pinned definition revision.");
        }
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE automation_run_claims SET
                settlement_kind=$kind,
                settlement_run_json=$runJson,
                settlement_next_run_at=$nextRunAt,
                settlement_error=$error,
                settled_at=$settledAt
            WHERE run_id=$runId AND claim_token=$token;
            """;
        command.Parameters.AddWithValue("$kind", (int)settlement.Kind);
        command.Parameters.AddWithValue("$runJson", (object?)settlement.RunJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$nextRunAt", (object?)settlement.NextRunAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)settlement.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$settledAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$runId", settlement.RunId.ToString());
        command.Parameters.AddWithValue("$token", settlement.ClaimToken);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        // The settlement must bind EXACTLY the held claim row whose token we hold. Zero updated rows
        // means the claim is missing or the token does not own it — a recording failure that must be
        // surfaced, never swallowed, so the exact operation stays unacknowledged and the claim retained.
        if (affected != 1)
            throw new InvalidOperationException("The uncertain settlement did not update exactly one held claim row.");
    }

    /// <summary>Reads retained claims whose settlement is still uncertain. Reconciliation replays only the
    /// exact persisted operation and releases the claim once it is durably acknowledged; it never
    /// re-executes the chart and never fabricates a fresh outcome.</summary>
    public async Task<IReadOnlyList<AutomationRunClaimSettlement>> GetUncertainSettlementsAsync(CancellationToken cancellationToken)
    {
        var result = new List<AutomationRunClaimSettlement>();
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT run_id, automation_id, claim_token, settlement_kind, settlement_run_json,
                   settlement_next_run_at, settlement_error
            FROM automation_run_claims
            WHERE settlement_kind IS NOT NULL
            ORDER BY settled_at;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var kindOrdinal = reader.GetOrdinal("settlement_kind");
            var nextOrdinal = reader.GetOrdinal("settlement_next_run_at");
            result.Add(new AutomationRunClaimSettlement(
                reader.Guid("run_id"),
                reader.Guid("automation_id"),
                reader.String("claim_token"),
                (AutomationRunClaimSettlementKind)reader.GetInt32(kindOrdinal),
                reader.NullableString("settlement_run_json"),
                reader.IsDBNull(nextOrdinal) ? null : reader.GetDateTimeOffset(nextOrdinal),
                reader.NullableString("settlement_error")));
        }
        return result;
    }

    /// <summary>Validates that a prepared persistence operation is bound to the exact held claim row
    /// owned by the token. The prepared Run must carry the SAME identity/automation, advance the claim's
    /// expected revision by exactly one, and stay pinned to the claim's definition revision. Used by
    /// reconciliation so a payload for another Run is never persisted while releasing this claim.</summary>
    public async Task<bool> ValidatePreparedOperationAgainstClaimAsync(Guid runId, string claimToken, AutomationRun prepared,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (runId == Guid.Empty || string.IsNullOrWhiteSpace(claimToken) || prepared.Id != runId) return false;
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var held = await ReadClaimRowAsync(connection, null, runId, claimToken, cancellationToken).ConfigureAwait(false);
        if (held is null || held.AutomationId != prepared.AutomationId || prepared.Revision != held.ExpectedRevision + 1) return false;
        return prepared.DefinitionRevision is { } defRev ? defRev == held.DefinitionRevision : held.DefinitionRevision is null;
    }

    private sealed record HeldClaimRow(Guid AutomationId, long ExpectedRevision, long? DefinitionRevision);

    /// <summary>Reads exactly the held claim row owned by the supplied token. Null when the claim is
    /// missing or the token does not own it. The claim's automation identity, expected revision and
    /// pinned definition revision are the authoritative fence for any prepared settlement operation.</summary>
    private static async Task<HeldClaimRow?> ReadClaimRowAsync(SqliteConnection connection, SqliteTransaction? transaction,
        Guid runId, string claimToken, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        if (transaction is not null) command.Transaction = transaction;
        command.CommandText = """
            SELECT automation_id, expected_revision, definition_revision
            FROM automation_run_claims
            WHERE run_id=$runId AND claim_token=$token;
            """;
        command.Parameters.AddWithValue("$runId", runId.ToString());
        command.Parameters.AddWithValue("$token", claimToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var defRevOrdinal = reader.GetOrdinal("definition_revision");
        return new(
            reader.Guid("automation_id"),
            reader.GetInt64(reader.GetOrdinal("expected_revision")),
            reader.IsDBNull(defRevOrdinal) ? null : reader.GetInt64(defRevOrdinal));
    }

    private static async Task<(bool? Enabled, string? LeaseToken)> ReadDefinitionStateAsync(
        SqliteConnection connection, SqliteTransaction transaction, Guid automationId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT is_enabled, lease_token FROM automations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", automationId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return (null, null);
        var enabled = reader.GetInt32(0) == 1;
        var lease = reader.IsDBNull(1) ? null : reader.GetString(1);
        return (enabled, lease);
    }

    private static string ComputeContinuationDigest(string? continuationJson)
    {
        var bytes = string.IsNullOrWhiteSpace(continuationJson)
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(continuationJson);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
