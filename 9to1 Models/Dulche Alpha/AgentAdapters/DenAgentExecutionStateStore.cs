using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NineToOne.Dulche.Den;

namespace Dulche.Runtime.Agents;

/// <summary>Persists the existing execution snapshot in the existing canonical Den run record.
/// This caller-bound adapter owns no second run registry and never restores execution authority.
/// Den's original atomic record publication, revision check, quota and history remain authoritative.</summary>
public sealed class DenAgentExecutionStateStore(DulcheDen den, string namespaceId) : IAgentExecutionStateStore
{
    private const string EnvelopeKey = "canonicalAgentExecution";
    private static readonly JsonSerializerOptions ExecutionJson = new(DenJson.Options)
    { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public async ValueTask<AgentExecutionSnapshot?> ReadAsync(string agentRunId, CancellationToken cancellationToken = default)
    {
        var record = await den.GetAsync<AgentRunRecord>(namespaceId, agentRunId, cancellationToken).ConfigureAwait(false);
        return record is null ? null : Decode(record).Snapshot;
    }

    public async ValueTask<IReadOnlyList<AgentRunSnapshot>> ListRunsAsync(string agentId, int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit < 1 || limit > 10_000) throw Invalid("The run listing limit must be between one and 10,000.");
        var records = await den.ListAsync<AgentRunRecord>(namespaceId, cancellationToken).ConfigureAwait(false);
        // Other Den run producers are preserved; only this explicitly versioned execution envelope is projected.
        return records.Where(record => record.AgentDefinitionId == agentId &&
                record.ExtensionData?.ContainsKey(EnvelopeKey) == true)
            .Select(record => Decode(record).Snapshot.Run)
            .OrderByDescending(run => run.CreatedAtUtc).ThenBy(run => run.AgentRunId, StringComparer.Ordinal)
            .Take(limit).ToArray();
    }

    public async ValueTask<AgentExecutionSnapshot> CommitAsync(AgentExecutionChangeSet changes,
        CancellationToken cancellationToken = default)
    {
        // Capture every caller-owned collection before any await. Returned/read records are separate snapshots.
        changes = JsonSerializer.Deserialize<AgentExecutionChangeSet>(JsonSerializer.SerializeToUtf8Bytes(changes, ExecutionJson), ExecutionJson)
            ?? throw Invalid("The execution change could not be captured.");
        if (changes.Validate() is { } invalid) throw Invalid(invalid.Message);
        var fingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(changes, ExecutionJson));
        var priorRecord = await den.GetAsync<AgentRunRecord>(namespaceId, changes.AgentRunId, cancellationToken).ConfigureAwait(false);
        var prior = priorRecord is null ? null : Decode(priorRecord);
        if (prior is not null && prior.Operations.TryGetValue(changes.OperationId, out var recorded))
        {
            if (recorded != fingerprint) throw new DenException(DenErrorCode.IdempotencyMismatch,
                "The execution operation ID was already used for different content.");
            await DemandExecuteAsync(prior.Snapshot.Run.AgentId, cancellationToken).ConfigureAwait(false);
            return prior.Snapshot;
        }
        if ((priorRecord?.Revision ?? 0) != changes.ExpectedRevision)
            throw new DenException(DenErrorCode.Conflict, "The canonical Den Agent run changed before this operation.", recoverable: true, retryable: true);
        var run = changes.Run ?? prior?.Snapshot.Run ?? throw Invalid("The initial execution commit requires its canonical run.");
        if (prior is not null && (run.AgentId != prior.Snapshot.Run.AgentId ||
            run.DefinitionRevision != prior.Snapshot.Run.DefinitionRevision || run.CallerId != prior.Snapshot.Run.CallerId ||
            run.SessionId != prior.Snapshot.Run.SessionId || run.CreatedAtUtc != prior.Snapshot.Run.CreatedAtUtc))
            throw Invalid("A canonical run cannot change its definition, originating caller, session or creation identity.");
        if (run.DefinitionRevision < 1 || string.IsNullOrWhiteSpace(run.AgentId) ||
            string.IsNullOrWhiteSpace(run.CallerId) || run.CallerId != den.PrincipalId || string.IsNullOrWhiteSpace(run.SessionId))
            throw new DenException(DenErrorCode.Forbidden, "The run does not match this original caller-bound Den session.");
        var definition = await den.GetAsync<AgentDefinitionRecord>(namespaceId, run.AgentId, cancellationToken).ConfigureAwait(false)
            ?? throw Invalid("The canonical Agent definition was not found in this namespace.");
        // Existing run history may record an older definition. Dispatch separately requires the exact current revision.
        if (prior is null && definition.Revision != run.DefinitionRevision)
            throw new DenException(DenErrorCode.Conflict, "The Agent definition changed before run creation.", recoverable: true, retryable: true);
        await DemandExecuteAsync(run.AgentId, cancellationToken).ConfigureAwait(false);
        var old = prior?.Snapshot;
        var events = MergeEvents(old?.Events ?? [], changes.Events ?? [], changes.AgentRunId, old?.LastEventSequence ?? 0);
        var completed = (old?.CompletedConsequentialActionIds ?? new HashSet<string>()).Concat(
            changes.CompletedConsequentialActionIds ?? new HashSet<string>()).ToHashSet(StringComparer.Ordinal);
        var uncertain = (old?.UncertainConsequentialActionIds ?? new HashSet<string>()).Concat(
            changes.UncertainConsequentialActionIds ?? new HashSet<string>()).Except(completed, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var next = new AgentExecutionSnapshot(AgentExecutionSnapshot.CurrentSchemaVersion,
            checked(changes.ExpectedRevision + 1), run,
            Merge(old?.QueueItems ?? [], changes.QueueItems, item => item.QueueItemId),
            Merge(old?.Blockers ?? [], changes.Blockers, item => item.BlockerId),
            Merge(old?.Approvals ?? [], changes.Approvals, item => item.ApprovalId),
            Merge(old?.Checkpoints ?? [], changes.Checkpoints, item => item.CheckpointId),
            Merge(old?.Outputs ?? [], changes.Outputs, item => item.Id),
            Merge(old?.Subagents ?? [], changes.Subagents, item => item.SubagentId),
            Merge(old?.Reservations ?? [], changes.Reservations, item => item.ReservationId),
            events, completed, uncertain, events.Count == 0 ? 0 : events[^1].Sequence);
        ValidateSnapshot(next);
        var operations = new Dictionary<string, string>(prior?.Operations ?? new Dictionary<string, string>(), StringComparer.Ordinal)
        { [changes.OperationId] = fingerprint };
        var extension = new Dictionary<string, JsonElement>(priorRecord?.ExtensionData ?? new Dictionary<string, JsonElement>(), StringComparer.Ordinal)
        { [EnvelopeKey] = JsonSerializer.SerializeToElement(new ExecutionEnvelope(1, den.Store.Manifest.DenId, namespaceId, next, operations), ExecutionJson) };
        var proposed = new AgentRunRecord
        {
            Id = run.AgentRunId, NamespaceId = namespaceId, Revision = Math.Max(1, changes.ExpectedRevision),
            CreatedAtUtc = run.CreatedAtUtc, UpdatedAtUtc = run.CreatedAtUtc,
            OriginDeviceId = priorRecord?.OriginDeviceId, ExtensionData = extension,
            AgentDefinitionId = run.AgentId, SessionId = run.SessionId, Status = run.State.ToString(),
            ParentRunId = run.ParentAgentRunId, SubagentRunIds = next.Subagents.Select(item => item.SubagentId).ToArray(),
            QueueItemIds = next.QueueItems.Select(item => item.QueueItemId).ToArray(),
            CheckpointIds = next.Checkpoints.Select(item => item.CheckpointId).ToArray(),
            BlockerIds = next.Blockers.Select(item => item.BlockerId).ToArray(),
            ApprovalIds = next.Approvals.Select(item => item.ApprovalId).ToArray(),
            ToolActionIds = completed.Order(StringComparer.Ordinal).ToArray(),
            RemainingBudgetJson = JsonSerializer.Serialize(run.BudgetLimits, ExecutionJson)
        };
        // Reuse Den's write ACL checks before/under its original lease/final publication. The extra
        // Execute check uses the same current policy and original Agent identity; it cannot admit tools.
        var writer = new DulcheDen(den.Store, new RunWritePolicy(den.AccessPolicy, den.PrincipalId, namespaceId,
            run.AgentRunId, run.AgentId), den.PrincipalId);
        var operationId = "agent-execution-" + Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new[] { den.Store.Manifest.DenId, namespaceId, changes.AgentRunId, changes.OperationId }, ExecutionJson)));
        var saved = await writer.SaveAsync(proposed, changes.ExpectedRevision, operationId, cancellationToken).ConfigureAwait(false);
        return Decode(saved).Snapshot;
    }

    private ExecutionEnvelope Decode(AgentRunRecord record)
    {
        if (record.ExtensionData is not { } extension || !extension.TryGetValue(EnvelopeKey, out var payload))
            throw Invalid("The Den run has no canonical execution envelope.");
        var envelope = payload.Deserialize<ExecutionEnvelope>(ExecutionJson) ?? throw Invalid("The execution envelope is unavailable.");
        if (envelope.Version != 1 || envelope.Snapshot is null || envelope.Operations is null ||
            envelope.DenId != den.Store.Manifest.DenId || envelope.NamespaceId != namespaceId ||
            record.NamespaceId != namespaceId || envelope.Snapshot.Revision != record.Revision ||
            envelope.Snapshot.Run.AgentRunId != record.Id || envelope.Snapshot.Run.AgentId != record.AgentDefinitionId ||
            envelope.Snapshot.Run.SessionId != record.SessionId || envelope.Snapshot.Run.State.ToString() != record.Status)
            throw Invalid("The execution envelope does not match its canonical Den record.");
        ValidateSnapshot(envelope.Snapshot);
        return envelope;
    }

    private async ValueTask DemandExecuteAsync(string agentId, CancellationToken cancellationToken)
    {
        if (!await den.AccessPolicy.IsAllowedAsync(den.PrincipalId, namespaceId, agentId,
                DenPermission.Execute, cancellationToken).ConfigureAwait(false))
            throw new DenException(DenErrorCode.Forbidden, "Current Agent execution permission is unavailable.");
    }

    private static IReadOnlyList<T> Merge<T>(IReadOnlyList<T> prior, IReadOnlyList<T>? changes, Func<T, string> identity)
    {
        if (changes is null) return prior;
        if (changes.Any(item => string.IsNullOrWhiteSpace(identity(item))) ||
            changes.Select(identity).Distinct(StringComparer.Ordinal).Count() != changes.Count)
            throw Invalid("An execution collection contains a missing or repeated identity.");
        var values = prior.ToDictionary(identity, StringComparer.Ordinal);
        foreach (var item in changes) values[identity(item)] = item;
        return values.Values.ToArray();
    }

    private static IReadOnlyList<AgentExecutionEventEnvelope> MergeEvents(IReadOnlyList<AgentExecutionEventEnvelope> prior,
        IReadOnlyList<AgentExecutionEventEnvelope> changes, string runId, long cursor)
    {
        foreach (var item in changes)
        {
            if (item.RootRequestId != runId || item.Sequence != checked(cursor + 1))
                throw Invalid("New execution events must retain the canonical root and contiguous original sequence.");
            cursor = item.Sequence;
        }
        return prior.Concat(changes).ToArray();
    }

    private static void ValidateSnapshot(AgentExecutionSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != AgentExecutionSnapshot.CurrentSchemaVersion || snapshot.Revision < 1 ||
            snapshot.Run is null || snapshot.Events is null || snapshot.Run.Attempts is null ||
            snapshot.QueueItems is null || snapshot.Blockers is null || snapshot.Approvals is null ||
            snapshot.Checkpoints is null || snapshot.Outputs is null || snapshot.Subagents is null ||
            snapshot.Reservations is null || snapshot.CompletedConsequentialActionIds is null ||
            snapshot.UncertainConsequentialActionIds is null ||
            string.IsNullOrWhiteSpace(snapshot.Run.AgentRunId) ||
            !snapshot.Run.Attempts.Any(attempt => attempt.AttemptId == snapshot.Run.CurrentAttemptId))
            throw Invalid("The canonical execution snapshot is invalid.");
        long cursor = 0;
        foreach (var item in snapshot.Events)
        {
            if (item.RootRequestId != snapshot.Run.AgentRunId || item.Sequence != checked(cursor + 1) ||
                !snapshot.Run.Attempts.Any(attempt => attempt.AttemptId == item.ExecutionId))
                throw Invalid("Stored execution events have an invalid root or sequence.");
            cursor = item.Sequence;
        }
        if (snapshot.LastEventSequence != cursor ||
            snapshot.QueueItems.Any(item => item.AgentId != snapshot.Run.AgentId || item.AgentRunId is { } id && id != snapshot.Run.AgentRunId) ||
            snapshot.Blockers.Any(item => item.AgentRunId != snapshot.Run.AgentRunId) ||
            snapshot.Approvals.Any(item => item.AgentRunId != snapshot.Run.AgentRunId) ||
            snapshot.Checkpoints.Any(item => item.AgentRunId != snapshot.Run.AgentRunId) ||
            snapshot.Subagents.Any(item => item.RootRequestId != snapshot.Run.AgentRunId) ||
            snapshot.Reservations.Any(item => item.RunId != snapshot.Run.AgentRunId))
            throw Invalid("Execution child records must retain their original canonical run scope.");
    }

    private sealed record ExecutionEnvelope(int Version, string DenId, string NamespaceId,
        AgentExecutionSnapshot Snapshot, IReadOnlyDictionary<string, string> Operations);

    private sealed class RunWritePolicy(IDenAccessPolicy original, string principalId, string namespaceId,
        string runId, string agentId) : IDenAccessPolicy
    {
        public async ValueTask<bool> IsAllowedAsync(string principal, string space, string objectId,
            DenPermission permission, CancellationToken cancellationToken = default)
        {
            if (principal != principalId || space != namespaceId || objectId != runId || permission != DenPermission.Write)
                return false;
            return await original.IsAllowedAsync(principal, space, objectId, permission, cancellationToken).ConfigureAwait(false) &&
                await original.IsAllowedAsync(principal, space, agentId, DenPermission.Execute, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static DenException Invalid(string message) => new(DenErrorCode.InvalidRecord, message);
}
