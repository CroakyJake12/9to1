using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Haven.Application.NodeGraph;

public sealed record GraphActivationRequest(string GraphType, Guid GraphId, long ExpectedRevision, Guid StartNodeId,
    string EventType, string SessionId, string EntityId, string ParticipantId, JsonElement Variables, int StepLimit = 1000);
public sealed record GraphRuntimeAdmission(GraphDocument Graph, GraphCapabilityProfile Capabilities);
public sealed record GraphRuntimeTransition(Guid NodeId, string State, JsonElement Values);

/// <summary>Explicit typed workflow outcomes. Legacy adapters leave Outcome unspecified; EffectiveOutcome infers.
/// These four states are distinct: completed, yielded (resumable), blocked (waiting on an external condition without
/// a claim to resume), and uncertain (needs recovery). No value is an authorization grant.</summary>
public enum GraphRunOutcome { Completed, Yielded, Blocked, Uncertain }

/// <summary>Continuation kind carried by an owning engine. Approval is distinct from Home permission and is never
/// auto-advanced by time alone; the owning integration supplies explicit typed approval/rejection/timeout/cancel.</summary>
public enum GraphContinuationKind { Wait, Approval, Custom }

/// <summary>Owner-captured continuation payload attached to a yielded/blocked result. This is descriptive state only —
/// never permission, a Home lease, an approved workflow decision, or a current actor receipt. The registry merges it
/// into a versioned envelope bound to the admitted graph/revision/profile/schema and the consumed budget.</summary>
public sealed record GraphContinuationCapture(string OwnerRunId, GraphContinuationKind Kind, Guid NodeId,
    DateTimeOffset? ResumeAt, JsonElement Condition, JsonElement OwnerState);

public sealed record GraphActivationResult(bool Succeeded, string Code, IReadOnlyList<GraphRuntimeTransition> Transitions,
    int StepsConsumed, bool NeedsRecovery = false)
{
    /// <summary>Explicit workflow outcome. Null is unspecified (legacy adapters); EffectiveOutcome infers</summary>
    public GraphRunOutcome? Outcome { get; init; }
    /// <summary>Owner-captured continuation data for yielded/blocked results (additive, opt-in).</summary>
    public GraphContinuationCapture? Continuation { get; init; }
    /// <summary>Detached, strictly versioned continuation envelope produced by the registry for yielded/blocked results.
    /// Ready only for durable storage; it is descriptive state, never execution authority.</summary>
    public GraphRuntimeContinuation? Envelope { get; init; }
    public GraphRunOutcome EffectiveOutcome => Outcome ?? (NeedsRecovery || !Succeeded
        ? GraphRunOutcome.Uncertain : GraphRunOutcome.Completed);
}

/// <summary>Owners authenticate session/participant/entity, read the canonical graph and narrow the profile in AdmitAsync.
/// ExecuteAsync uses owning typed operations and rechecks their live ACL/revision before side effects. Every evaluated
/// node/event consumes one budget step. A graph activation cannot itself confer resource or mutation permission.</summary>
public interface INodeGraphRuntimeAdapter
{
    string GraphType { get; }
    IReadOnlySet<string> SupportedEvents { get; }
    ValueTask<GraphRuntimeAdmission?> AdmitAsync(GraphActivationRequest request, CancellationToken cancellationToken);
    ValueTask<GraphActivationResult> ExecuteAsync(GraphActivationRequest request, GraphRuntimeAdmission admission,
        GraphExecutionBudget budget, CancellationToken cancellationToken);
}

/// <summary>Opt-in continuation capability. Old adapters that implement only INodeGraphRuntimeAdapter keep the original
/// activation path; resume against them fails clearly before any effect. The registry re-admits and re-validates the
/// pinned graph/revision/schema/profile binding on every resume before invoking this callback.</summary>
public interface INodeGraphContinuationAdapter : INodeGraphRuntimeAdapter
{
    ValueTask<GraphActivationResult> ResumeAsync(GraphRuntimeContinuation continuation, GraphActivationRequest request,
        GraphRuntimeAdmission admission, GraphExecutionBudget budget, CancellationToken cancellationToken);
}

public sealed class GraphExecutionBudget
{
    private int _consumed;
    public int Limit { get; }
    public int StepsConsumed => Volatile.Read(ref _consumed);
    internal GraphExecutionBudget(int limit) : this(limit, 0) { }
    internal GraphExecutionBudget(int limit, int consumed)
    {
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        if (consumed < 0 || consumed > limit) throw new ArgumentOutOfRangeException(nameof(consumed));
        Limit = limit;
        _consumed = consumed;
    }
    public bool TryConsume(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            var prior = StepsConsumed;
            if (prior >= Limit) return false;
            if (Interlocked.CompareExchange(ref _consumed, prior + 1, prior) == prior) return true;
        }
    }
}

/// <summary>Shared activation boundary. Cyclic/reactive graphs are allowed; evaluation remains in the owning adapter.</summary>
public sealed class NodeGraphRuntimeRegistry(NodeGraphSchemaRegistry schemas, IEnumerable<INodeGraphRuntimeAdapter> adapters)
{
    private readonly INodeGraphRuntimeAdapter[] _adapters = adapters.ToArray();

    public async ValueTask<GraphActivationResult> ActivateAsync(GraphActivationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsBasicRequestValid(request)) return Reject("graph.activation.invalid");
        request = request with { Variables = request.Variables.Clone() };
        var adapter = FindAdapter(request.GraphType);
        if (adapter is null) return Reject("graph.runtime.unavailable-or-ambiguous");
        if (!adapter.SupportedEvents.Contains(request.EventType)) return Reject("graph.event.unsupported");
        var admitted = await adapter.AdmitAsync(request, cancellationToken).ConfigureAwait(false);
        var reject = ValidateAdmission(schemas, admitted, request, out var normalized);
        if (reject is not null) return Reject(reject);
        var budget = new GraphExecutionBudget(request.StepLimit);
        var result = await adapter.ExecuteAsync(request, normalized!, budget, cancellationToken).ConfigureAwait(false);
        return FinalizeResult(result, request, normalized!, budget);
    }

    /// <summary>Resume a persisted continuation at the SAME registry boundary. The caller supplies a fresh authenticated
    /// context; the registry re-admits and re-validates the exact pinned graph/revision/schema/profile before dispatching.
    /// A serialized envelope is descriptive state, never permission, a lease, an approved decision or an actor receipt.</summary>
    public async ValueTask<GraphActivationResult> ResumeAsync(GraphRuntimeContinuation continuation,
        GraphActivationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (continuation.ProtocolVersion != GraphRuntimeContinuation.CurrentProtocolVersion) return Reject("graph.resume.version.unsupported");
        if (continuation.StepLimit is < 1 or > 10000 || continuation.StepsConsumed < 0 || continuation.StepsConsumed > continuation.StepLimit)
            return Reject("graph.resume.budget.tampered");
        if (continuation.NodeId == Guid.Empty || string.IsNullOrWhiteSpace(continuation.OwnerRunId))
            return Reject("graph.resume.envelope.invalid");
        if (continuation.Kind is not (GraphContinuationKind.Wait or GraphContinuationKind.Approval or GraphContinuationKind.Custom))
            return Reject("graph.resume.kind.unsupported");
        if (continuation.Condition.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined) ||
            continuation.OwnerState.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined))
            return Reject("graph.resume.state.malformed");
        if (!IsBasicRequestValid(request)) return Reject("graph.resume.request.invalid");
        if (!string.Equals(request.GraphType, continuation.GraphType, StringComparison.Ordinal) ||
            request.GraphId != continuation.GraphId || request.ExpectedRevision != continuation.Revision ||
            request.StartNodeId != continuation.StartNodeId ||
            !string.Equals(request.EventType, continuation.EventType, StringComparison.Ordinal) ||
            !string.Equals(request.SessionId, continuation.SessionId, StringComparison.Ordinal) ||
            !string.Equals(request.EntityId, continuation.EntityId, StringComparison.Ordinal) ||
            !string.Equals(request.ParticipantId, continuation.ParticipantId, StringComparison.Ordinal))
            return Reject("graph.resume.caller-mismatch");
        request = request with { Variables = request.Variables.Clone() };
        var adapter = FindAdapter(request.GraphType);
        if (adapter is null) return Reject("graph.runtime.unavailable-or-ambiguous");
        if (adapter is not INodeGraphContinuationAdapter continuationAdapter) return Reject("graph.resume.unsupported");
        if (!adapter.SupportedEvents.Contains(request.EventType)) return Reject("graph.event.unsupported");
        var admitted = await adapter.AdmitAsync(request, cancellationToken).ConfigureAwait(false);
        var reject = ValidateAdmission(schemas, admitted, request, out var normalized);
        if (reject is not null) return Reject(reject);
        if (normalized!.Graph.CapabilityProfileId != continuation.CapabilityProfileId ||
            normalized.Graph.SchemaVersion != continuation.SchemaVersion ||
            normalized.Graph.GraphId != continuation.GraphId || normalized.Graph.Revision != continuation.Revision)
            return Reject("graph.resume.descriptor-mismatch");
        if (!normalized.Graph.Nodes.Any(node => node.NodeId == continuation.NodeId)) return Reject("graph.resume.node.missing");
        var digest = GraphContinuationDigest.Compute(normalized.Graph, request.Variables);
        if (!FixedTimeEquals(digest, continuation.Digest)) return Reject("graph.resume.digest-mismatch");
        var budget = new GraphExecutionBudget(continuation.StepLimit, continuation.StepsConsumed);
        var result = await continuationAdapter.ResumeAsync(continuation, request, normalized, budget, cancellationToken).ConfigureAwait(false);
        return FinalizeResult(result, request, normalized, budget);
    }

    private GraphActivationResult FinalizeResult(GraphActivationResult? result, GraphActivationRequest request,
        GraphRuntimeAdmission admission, GraphExecutionBudget budget)
    {
        // Invalid acknowledgement may follow owner side effects. Preserve uncertainty instead of claiming no execution.
        if (result is null || result.Transitions is null || result.Transitions.Count > budget.Limit ||
            result.StepsConsumed != budget.StepsConsumed || result.Transitions.Count > budget.StepsConsumed ||
            result.Transitions.Any(transition => transition is null || transition.Values.ValueKind == JsonValueKind.Undefined ||
                !admission.Graph.Nodes.Any(node => node.NodeId == transition.NodeId)))
            return new(false, "graph.result.needs-recovery", [], budget.StepsConsumed, true);
        var captureError = CaptureContinuation(result, request, admission, budget, out var envelope);
        if (captureError is not null) return captureError;
        return result with
        {
            Transitions = Array.AsReadOnly(result.Transitions.Select(transition => transition with
                { Values = transition.Values.Clone() }).ToArray()),
            Outcome = result.Outcome,
            NeedsRecovery = result.EffectiveOutcome == GraphRunOutcome.Uncertain || result.NeedsRecovery,
            Envelope = envelope
        };
    }

    private static GraphActivationResult? CaptureContinuation(GraphActivationResult result, GraphActivationRequest request,
        GraphRuntimeAdmission admission, GraphExecutionBudget budget, out GraphRuntimeContinuation? envelope)
    {
        envelope = null;
        var outcome = result.EffectiveOutcome;
        if (outcome is not (GraphRunOutcome.Yielded or GraphRunOutcome.Blocked)) return null;
        if (result.Continuation is not { } capture)
            return new(false, "graph.result.missing-continuation", [], budget.StepsConsumed, true);
        if (string.IsNullOrWhiteSpace(capture.OwnerRunId) || capture.NodeId == Guid.Empty ||
            capture.Kind is not (GraphContinuationKind.Wait or GraphContinuationKind.Approval or GraphContinuationKind.Custom) ||
            capture.Condition.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined) ||
            capture.OwnerState.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined) ||
            !admission.Graph.Nodes.Any(node => node.NodeId == capture.NodeId))
            return new(false, "graph.result.continuation.invalid", [], budget.StepsConsumed, true);
        var digest = GraphContinuationDigest.Compute(admission.Graph, request.Variables);
        envelope = new GraphRuntimeContinuation(
            GraphRuntimeContinuation.CurrentProtocolVersion,
            request.GraphType,
            capture.OwnerRunId,
            request.GraphId,
            request.ExpectedRevision,
            admission.Graph.CapabilityProfileId,
            admission.Graph.SchemaVersion,
            request.StartNodeId,
            request.EventType,
            request.SessionId,
            request.EntityId,
            request.ParticipantId,
            capture.Kind,
            capture.NodeId,
            capture.ResumeAt,
            capture.Condition.Clone(),
            capture.OwnerState.Clone(),
            budget.StepsConsumed,
            budget.Limit,
            digest);
        return null;
    }

    private INodeGraphRuntimeAdapter? FindAdapter(string graphType)
    {
        var matching = _adapters.Where(adapter => adapter.GraphType == graphType).ToArray();
        return matching.Length == 1 ? matching[0] : null;
    }

    private static bool IsBasicRequestValid(GraphActivationRequest request) =>
        !string.IsNullOrWhiteSpace(request.GraphType) && request.GraphId != Guid.Empty && request.ExpectedRevision >= 1 &&
        request.StartNodeId != Guid.Empty && !string.IsNullOrWhiteSpace(request.EventType) && !string.IsNullOrWhiteSpace(request.SessionId) &&
        !string.IsNullOrWhiteSpace(request.EntityId) && !string.IsNullOrWhiteSpace(request.ParticipantId) &&
        request.Variables.ValueKind == JsonValueKind.Object && request.StepLimit is >= 1 and <= 10000;

    private static string? ValidateAdmission(NodeGraphSchemaRegistry schemas, GraphRuntimeAdmission? admitted,
        GraphActivationRequest request, out GraphRuntimeAdmission? normalized)
    {
        normalized = null;
        if (admitted is null) return "graph.activation.denied";
        var graph = admitted.Graph;
        var profile = admitted.Capabilities;
        if (graph is null || profile is null || graph.Nodes is null || graph.Connections is null ||
            graph.Nodes.Count > 10000 || graph.Connections.Count > 50000 ||
            graph.Nodes.Any(node => node is null || node.Configuration.ValueKind != JsonValueKind.Object || node.Ports is null ||
                node.Ports.Any(port => port is null)) ||
            graph.Connections.Any(connection => connection is null) || profile.AllowedNodeTypes is null || profile.AllowedCapabilityIds is null)
            return "graph.admission.invalid";
        graph = graph with { Nodes = Array.AsReadOnly(graph.Nodes.Select(node => node with
            { Configuration = node.Configuration.Clone(), Ports = Array.AsReadOnly(node.Ports.ToArray()) }).ToArray()),
            Connections = Array.AsReadOnly(graph.Connections.ToArray()) };
        profile = profile with { AllowedNodeTypes = profile.AllowedNodeTypes.ToFrozenSet(StringComparer.Ordinal),
            AllowedCapabilityIds = profile.AllowedCapabilityIds.ToFrozenSet(StringComparer.Ordinal) };
        if (graph.GraphId != request.GraphId || graph.Revision != request.ExpectedRevision || graph.State != GraphRevisionState.Active ||
            graph.CapabilityProfileId != profile.ProfileId || !graph.Nodes.Any(node => node.NodeId == request.StartNodeId))
            return "graph.activation.stale-or-inactive";
        if (schemas.Validate(graph).Count != 0 || graph.Nodes.Any(node => !profile.AllowedNodeTypes.Contains(node.TypeId) ||
            !profile.AllowedCapabilityIds.Contains(node.CapabilityId))) return "graph.activation.incompatible-or-denied";
        normalized = new(graph, profile);
        return null;
    }

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static GraphActivationResult Reject(string code) => new(false, code, [], 0);
}
