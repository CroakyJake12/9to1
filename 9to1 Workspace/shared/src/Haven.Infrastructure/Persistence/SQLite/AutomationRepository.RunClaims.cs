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
    /// stale view nor caller-supplied JSON ever becomes the fence. A conflict on the run_id primary key
    /// makes exactly one concurrent caller win; every loser acquires nothing and performs zero effects.</summary>
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

        // The definition must still exist and be operational; a disabled/deleted/revoked definition is
        // never resumable and the run stops before any side effect.
        var enabled = await ReadDefinitionEnabledAsync(connection, transaction, request.AutomationId, cancellationToken).ConfigureAwait(false);
        if (enabled is null)
            return new(false, string.Empty, AutomationResumeErrorKind.NeedsAttention, "definition.deleted", "The definition is gone; the Run cannot be resumed.");
        if (enabled.Value != true)
            return new(false, string.Empty, AutomationResumeErrorKind.NeedsAttention, "definition.disabled", "The definition is disabled; the Run cannot be resumed.");

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
    /// refused so a losing/stale worker cannot steal another winner's claim.</summary>
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

    private static async Task<bool?> ReadDefinitionEnabledAsync(
        SqliteConnection connection, SqliteTransaction transaction, Guid automationId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT is_enabled FROM automations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", automationId.ToString());
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null or DBNull) return null;
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private static string ComputeContinuationDigest(string? continuationJson)
    {
        var bytes = string.IsNullOrWhiteSpace(continuationJson)
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(continuationJson);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
