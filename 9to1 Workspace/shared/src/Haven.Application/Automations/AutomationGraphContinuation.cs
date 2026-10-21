using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Haven.Application.Automations;

/// <summary>The kind of durable suspension a graph node can yield.</summary>
public enum AutomationGraphYieldKind
{
    /// <summary>A durable Wait/Delay. The run releases execution resources and is resumed when the
    /// due time/condition is observed. ResumeAt/condition metadata is stored inspectably.</summary>
    Wait = 0,
    /// <summary>A first-class Approval/Human Review pause. The run waits for an explicit decision
    /// (approved/rejected/timed-out/cancelled). Being due is only a candidate, never an approval.</summary>
    Approval = 1
}

/// <summary>Typed, inspectable yield produced by a node. It is never a claim and never an approval:
/// it only describes the intended durable suspension the owning runtime must persist. The identity of
/// the node that yielded is always recorded so resume can satisfy the SAME pending wait once and never
/// recompute a moving due time.</summary>
public sealed record AutomationGraphYield(
    AutomationGraphYieldKind Kind,
    Guid? YieldNodeId = null,
    DateTimeOffset? ResumeAt = null,
    string? Condition = null,
    string? ApprovalActionJson = null,
    Guid? ApprovalNodeId = null);

/// <summary>A typed output value bound to a node/port identity, preserving missing states.</summary>
public sealed record AutomationGraphTypedOutput(Guid NodeId, string PortId, AutomationGraphValue Value);

/// <summary>Detached, strictly versioned owner execution-state snapshot for durable Wait/Approval
/// resume. It is the owning side payload that carries the canonical graph identity (GraphID/pinned
/// definition revision/schema/source digest), the full prior audit trace (completed-node identities,
/// states, selected branches and typed false/null/absent/unavailable/redacted/error values), the exact
/// next frontier/order, the typed input/output environment, consumed budget, original timestamps and
/// the pending yield. A restarted process resumes from this snapshot instead of restarting the graph
/// from the beginning, so already-committed node side effects are never repeated. This owner payload
/// rides on (and will be consolidated into) the reviewed shared Node Graph envelope owned by the Graph
/// worker; it is never a competing shared framework.</summary>
public sealed record AutomationGraphContinuation(
    int Version,
    Guid GraphId,
    long PinnedDefinitionRevision,
    int SchemaVersion,
    string SourceDigest,
    DateTimeOffset StartedAt,
    IReadOnlyList<AutomationGraphNodeTrace> Trace,
    IReadOnlyList<Guid> Frontier,
    IReadOnlyList<Guid> ActivatedEdges,
    IReadOnlyDictionary<Guid, string?> Outputs,
    IReadOnlyList<AutomationGraphTypedOutput> TypedOutputs,
    int ConsumedBudget,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    int AuthorizedStepLimit,
    AutomationGraphRunMode Mode,
    DateTimeOffset? ResumeAt,
    AutomationGraphYield? PendingYield,
    string? ApprovalDecision = null)
{
    public const int CurrentVersion = 1;
    public static readonly IReadOnlySet<string> ApprovalDecisions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "approved", "rejected", "timed-out", "cancelled" };

    public static AutomationGraphContinuation Empty { get; } = new(
        CurrentVersion, Guid.Empty, 0, 0, string.Empty, DateTimeOffset.MinValue,
        [], [], [], new Dictionary<Guid, string?>(), [], 0, 0, AutomationGraphRunMode.Real, null, null);

    /// <summary>The node ids recorded as finished (succeeded, failed or skipped) in the prior trace.</summary>
    public IReadOnlySet<Guid> FinishedNodeIds => Trace.Select(item => item.NodeId).ToHashSet();

    /// <summary>Deterministic digest of the detached continuation payload, used as the atomic claim
    /// fence so a losing resume worker cannot both acquire the same continuation and race at a
    /// terminal CAS.</summary>
    public string ComputeDigest() =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AutomationGraphContinuationCodec.Serialize(this)))).ToLowerInvariant();

    /// <summary>Structural validation independent of JSON encoding. Returns non-empty issues when the
    /// snapshot cannot be trusted to resume (unknown version, missing identity, inconsistent frontier,
    /// or a pending yield without yield metadata).</summary>
    public IReadOnlyList<AutomationGraphValidationIssue> Validate()
    {
        var issues = new List<AutomationGraphValidationIssue>();
        if (Version != CurrentVersion) issues.Add(new("continuation.version.unsupported", $"Unsupported continuation version {Version}."));
        if (GraphId == Guid.Empty) issues.Add(new("continuation.graph.missing", "A durable continuation must carry its canonical GraphID."));
        if (PinnedDefinitionRevision < 1) issues.Add(new("continuation.revision.missing", "A durable continuation must carry its pinned definition revision."));
        if (SchemaVersion < 1) issues.Add(new("continuation.schema.missing", "A durable continuation must carry its source schema version."));
        if (string.IsNullOrWhiteSpace(SourceDigest)) issues.Add(new("continuation.digest.missing", "A durable continuation must carry its source digest."));
        if (StartedAt == default) issues.Add(new("continuation.started.missing", "A durable continuation must carry its original start timestamp."));
        if (Mode != AutomationGraphRunMode.Real) issues.Add(new("continuation.mode.invalid", "A durable continuation must be a real run, never a test preview."));
        if (Trace.Select(item => item.NodeId).Distinct().Count() != Trace.Count)
            issues.Add(new("continuation.trace.duplicate", "A durable continuation trace must not repeat a node identity."));
        if (PendingYield is { YieldNodeId: null } || PendingYield is { Kind: AutomationGraphYieldKind.Approval, ApprovalNodeId: null })
            issues.Add(new("continuation.yield.missing", "A durable continuation must record the node that yielded."));
        if (ResumeAt is null && PendingYield is { Kind: AutomationGraphYieldKind.Wait }) issues.Add(new("continuation.resumeat.missing", "A Wait continuation must carry an inspectable ResumeAt."));
        if (PendingYield is { Kind: AutomationGraphYieldKind.Approval } && string.IsNullOrWhiteSpace(PendingYield.ApprovalActionJson))
            issues.Add(new("continuation.approval.missing", "An Approval continuation must declare the intended decision/action."));
        if (ApprovalDecision is not null && !ApprovalDecisions.Contains(ApprovalDecision))
            issues.Add(new("continuation.approval.decision.invalid", $"The recorded approval decision '{ApprovalDecision}' is not a known decision."));
        return issues;
    }
}

/// <summary>The outcome of running a graph from a continuation: either a terminal run result or a
/// durable suspension that produced a persisted-ready snapshot.</summary>
public abstract record AutomationGraphRunOutcome;

public sealed record AutomationGraphRunTerminal(AutomationGraphRunResult Result) : AutomationGraphRunOutcome;

public sealed record AutomationGraphRunYield(AutomationGraphContinuation Continuation) : AutomationGraphRunOutcome;

/// <summary>Strict, versioned codec for the owner <see cref="AutomationGraphContinuation"/> payload.
/// This is the owning execution-state payload that rides on the reviewed shared Node Graph envelope
/// (owned by the Graph worker); it is not a competing shared framework. Integrity is enforced by
/// re-serializing and verifying structural invariants on read, and by a serialized size cap so a
/// corrupt/oversized/tampered continuation is refused before any side effect. Serialization round-trip
/// determinism is a tamper/consistency check, not an authentication grant — live authority is always
/// revalidated by the owning admission/action paths on resume.</summary>
public static class AutomationGraphContinuationCodec
{
    public const int CurrentVersion = 1;
    private const string Prefix = "haven:automation-graph-continuation:v1:";
    private const int MaximumSerializedCharacters = 262_144;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(AutomationGraphContinuation continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        if (continuation.Version != CurrentVersion) throw new InvalidOperationException($"Unsupported continuation version {continuation.Version}.");
        var json = JsonSerializer.Serialize(continuation, Options);
        if (json.Length > MaximumSerializedCharacters) throw new InvalidOperationException("Continuation exceeds the maximum serialized size.");
        return Prefix + json;
    }

    public static bool IsContinuation(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

    public static bool TryDeserialize(string? value, out AutomationGraphContinuation continuation)
    {
        continuation = AutomationGraphContinuation.Empty;
        if (!IsContinuation(value)) return false;
        var payload = value![Prefix.Length..];
        if (payload.Length > MaximumSerializedCharacters) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<AutomationGraphContinuation>(payload, Options);
            if (parsed is null || parsed.Validate().Count != 0) return false;
            // Round-trip determinism: the detached payload must re-serialize to the exact same bytes,
            // so a tampered/hand-edited continuation cannot smuggle a different revision/digest through.
            if (!string.Equals(JsonSerializer.Serialize(parsed, Options), payload, StringComparison.Ordinal)) return false;
            continuation = parsed;
            return true;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
