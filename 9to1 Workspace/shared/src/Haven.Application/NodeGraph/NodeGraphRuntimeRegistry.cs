using System.Collections.Frozen;
using System.Text.Json;

namespace Haven.Application.NodeGraph;

public sealed record GraphActivationRequest(string GraphType, Guid GraphId, long ExpectedRevision, Guid StartNodeId,
    string EventType, string SessionId, string EntityId, string ParticipantId, JsonElement Variables, int StepLimit = 1000);
public sealed record GraphRuntimeAdmission(GraphDocument Graph, GraphCapabilityProfile Capabilities);
public sealed record GraphRuntimeTransition(Guid NodeId, string State, JsonElement Values);
public sealed record GraphActivationResult(bool Succeeded, string Code, IReadOnlyList<GraphRuntimeTransition> Transitions,
    int StepsConsumed, bool NeedsRecovery = false);

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

public sealed class GraphExecutionBudget
{
    private int _consumed;
    public int Limit { get; }
    public int StepsConsumed => Volatile.Read(ref _consumed);
    internal GraphExecutionBudget(int limit) => Limit = limit;
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
        if (string.IsNullOrWhiteSpace(request.GraphType) || request.GraphId == Guid.Empty || request.ExpectedRevision < 1 ||
            request.StartNodeId == Guid.Empty || string.IsNullOrWhiteSpace(request.EventType) || string.IsNullOrWhiteSpace(request.SessionId) ||
            string.IsNullOrWhiteSpace(request.EntityId) || string.IsNullOrWhiteSpace(request.ParticipantId) ||
            request.Variables.ValueKind != JsonValueKind.Object || request.StepLimit is < 1 or > 10000)
            return Reject("graph.activation.invalid");
        request = request with { Variables = request.Variables.Clone() };
        var matching = _adapters.Where(adapter => adapter.GraphType == request.GraphType).ToArray();
        if (matching.Length != 1) return Reject("graph.runtime.unavailable-or-ambiguous");
        var adapter = matching[0];
        if (!adapter.SupportedEvents.Contains(request.EventType)) return Reject("graph.event.unsupported");
        var admitted = await adapter.AdmitAsync(request, cancellationToken).ConfigureAwait(false);
        if (admitted is null) return Reject("graph.activation.denied");
        var graph = admitted.Graph;
        var profile = admitted.Capabilities;
        if (graph is null || profile is null || graph.Nodes is null || graph.Connections is null ||
            graph.Nodes.Count > 10000 || graph.Connections.Count > 50000 ||
            graph.Nodes.Any(node => node is null || node.Configuration.ValueKind != JsonValueKind.Object || node.Ports is null || node.Ports.Any(port => port is null)) ||
            graph.Connections.Any(connection => connection is null) || profile.AllowedNodeTypes is null || profile.AllowedCapabilityIds is null)
            return Reject("graph.admission.invalid");
        graph = graph with { Nodes = Array.AsReadOnly(graph.Nodes.Select(node => node with
            { Configuration = node.Configuration.Clone(), Ports = Array.AsReadOnly(node.Ports.ToArray()) }).ToArray()),
            Connections = Array.AsReadOnly(graph.Connections.ToArray()) };
        profile = profile with { AllowedNodeTypes = profile.AllowedNodeTypes.ToFrozenSet(StringComparer.Ordinal),
            AllowedCapabilityIds = profile.AllowedCapabilityIds.ToFrozenSet(StringComparer.Ordinal) };
        if (graph.GraphId != request.GraphId || graph.Revision != request.ExpectedRevision || graph.State != GraphRevisionState.Active ||
            graph.CapabilityProfileId != profile.ProfileId || !graph.Nodes.Any(node => node.NodeId == request.StartNodeId))
            return Reject("graph.activation.stale-or-inactive");
        if (schemas.Validate(graph).Count != 0 || graph.Nodes.Any(node => !profile.AllowedNodeTypes.Contains(node.TypeId) ||
            !profile.AllowedCapabilityIds.Contains(node.CapabilityId))) return Reject("graph.activation.incompatible-or-denied");
        var budget = new GraphExecutionBudget(request.StepLimit);
        var result = await adapter.ExecuteAsync(request, new(graph, profile), budget, cancellationToken).ConfigureAwait(false);
        // Invalid acknowledgement may follow owner side effects. Preserve uncertainty instead of claiming no execution.
        if (result is null || result.Transitions is null || result.Transitions.Count > request.StepLimit ||
            result.StepsConsumed != budget.StepsConsumed || result.Transitions.Count > budget.StepsConsumed ||
            result.Transitions.Any(transition => transition is null || transition.Values.ValueKind == JsonValueKind.Undefined || !graph.Nodes.Any(node => node.NodeId == transition.NodeId)))
            return new(false, "graph.result.needs-recovery", [], budget.StepsConsumed, true);
        return result with { Transitions = Array.AsReadOnly(result.Transitions.Select(transition => transition with
            { Values = transition.Values.Clone() }).ToArray()) };
    }
    private static GraphActivationResult Reject(string code) => new(false, code, [], 0);
}
