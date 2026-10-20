namespace Haven.Application.Automations;

public enum AutomationGraphRunMode
{
    Real = 0,
    Test = 1
}

public enum AutomationGraphTraceStatus
{
    Succeeded = 0,
    Failed = 1,
    Skipped = 2
}

public sealed record AutomationGraphValidationIssue(string Code, string Message, Guid? NodeId = null, Guid? EdgeId = null);
public sealed record AutomationGraphNodeExecutionContext(
    AutomationGraphNodeDefinition Node,
    AutomationGraphRunMode Mode,
    IReadOnlyDictionary<Guid, string?> Inputs,
    IReadOnlyDictionary<string, IReadOnlyList<AutomationGraphValue>>? TypedInputs = null);
public sealed record AutomationGraphNodeExecutionResult(
    bool Succeeded,
    string Message,
    string? Output = null,
    string? Branch = null,
    IReadOnlyDictionary<string, AutomationGraphValue>? TypedOutputs = null)
{
    /// <summary>When set, the node yielded a durable Wait/Approval suspension instead of completing.
    /// The owning runtime persists the continuation and releases execution resources; it never sleeps
    /// in-process and never treats the yield as a completed side effect.</summary>
    public AutomationGraphYield? Yield { get; init; }
}
public sealed record AutomationGraphNodeTrace(
    Guid NodeId,
    string Category,
    AutomationGraphTraceStatus Status,
    string Message,
    string? Output = null,
    string? Branch = null,
    Dictionary<Guid, string?>? Inputs = null,
    IReadOnlyDictionary<string, IReadOnlyList<AutomationGraphValue>>? TypedInputs = null,
    IReadOnlyDictionary<string, AutomationGraphValue>? TypedOutputs = null);
public sealed record AutomationGraphRunResult(AutomationGraphRunMode Mode, bool Succeeded, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, IReadOnlyList<AutomationGraphValidationIssue> ValidationIssues, IReadOnlyList<AutomationGraphNodeTrace> Trace, string? FailureMessage = null);

public interface IAutomationGraphNodeExecutor
{
    bool CanExecute(AutomationGraphNodeDefinition node);
    Task<AutomationGraphNodeExecutionResult> ExecuteAsync(AutomationGraphNodeExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>Validates a graph completely before deterministically executing any node.</summary>
public sealed class AutomationGraphRunner(IEnumerable<IAutomationGraphNodeExecutor> executors)
{
    private readonly IReadOnlyList<IAutomationGraphNodeExecutor> _executors = executors?.ToArray() ?? [];

    public IReadOnlyList<AutomationGraphValidationIssue> Validate(AutomationGraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var issues = new List<AutomationGraphValidationIssue>();
        var nodes = graph.Nodes ?? [];
        var edges = graph.Edges ?? [];
        if (nodes.Count == 0) issues.Add(new("graph.empty", "The workflow graph does not contain any nodes."));

        var ids = new HashSet<Guid>();
        foreach (var node in nodes)
        {
            if (node.Id == Guid.Empty) issues.Add(new("node.id.empty", "Workflow nodes must have a stable ID.", node.Id));
            else if (!ids.Add(node.Id)) issues.Add(new("node.id.duplicate", $"Workflow node ID {node.Id} is duplicated.", node.Id));
            if (string.IsNullOrWhiteSpace(node.Category)) issues.Add(new("node.category.missing", "Workflow nodes must declare a category.", node.Id));

            var portIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var port in node.EffectivePorts)
            {
                if (string.IsNullOrWhiteSpace(port.Id)) issues.Add(new("port.id.missing", "Node ports must have an ID.", node.Id));
                else if (!portIds.Add(port.Id)) issues.Add(new("port.id.duplicate", $"Port '{port.Id}' is duplicated on this node.", node.Id));
            }
            if (!IsBuiltIn(node) && !_executors.Any(executor => executor.CanExecute(node))) issues.Add(new("node.unsupported", $"No runtime executor is registered for node category '{node.Category}'.", node.Id));
        }

        var nodeById = nodes.Where(node => node.Id != Guid.Empty).GroupBy(node => node.Id).ToDictionary(group => group.Key, group => group.First());
        var edgeIds = new HashSet<Guid>();
        foreach (var edge in edges)
        {
            if (edge.Id != Guid.Empty && !edgeIds.Add(edge.Id)) issues.Add(new("edge.id.duplicate", $"Workflow edge ID {edge.Id} is duplicated.", EdgeId: edge.Id));
            if (!nodeById.TryGetValue(edge.FromNodeId, out var from)) { issues.Add(new("edge.from.missing", "An edge starts at a node that does not exist.", EdgeId: edge.Id)); continue; }
            if (!nodeById.TryGetValue(edge.ToNodeId, out var to)) { issues.Add(new("edge.to.missing", "An edge ends at a node that does not exist.", EdgeId: edge.Id)); continue; }
            if (edge.FromNodeId == edge.ToNodeId) issues.Add(new("edge.self", "A workflow node cannot connect directly to itself.", edge.FromNodeId, edge.Id));

            var fromPort = from.EffectivePorts.FirstOrDefault(port => string.Equals(port.Id, edge.FromPortId, StringComparison.OrdinalIgnoreCase));
            var toPort = to.EffectivePorts.FirstOrDefault(port => string.Equals(port.Id, edge.ToPortId, StringComparison.OrdinalIgnoreCase));
            if (fromPort is null) issues.Add(new("edge.from-port.missing", $"Output port '{edge.FromPortId}' does not exist.", from.Id, edge.Id));
            else if (fromPort.Direction != AutomationGraphPortDirection.Output) issues.Add(new("edge.from-port.direction", $"Port '{edge.FromPortId}' is not an output port.", from.Id, edge.Id));
            if (toPort is null) issues.Add(new("edge.to-port.missing", $"Input port '{edge.ToPortId}' does not exist.", to.Id, edge.Id));
            else if (toPort.Direction != AutomationGraphPortDirection.Input) issues.Add(new("edge.to-port.direction", $"Port '{edge.ToPortId}' is not an input port.", to.Id, edge.Id));
            if (fromPort is not null && toPort is not null && !string.Equals(fromPort.DataType, "any", StringComparison.OrdinalIgnoreCase) && !string.Equals(toPort.DataType, "any", StringComparison.OrdinalIgnoreCase) && !string.Equals(fromPort.DataType, toPort.DataType, StringComparison.OrdinalIgnoreCase)) issues.Add(new("edge.type", $"Port types '{fromPort.DataType}' and '{toPort.DataType}' are incompatible.", to.Id, edge.Id));
        }

        foreach (var group in edges.GroupBy(edge => (edge.ToNodeId, Port: edge.ToPortId.ToUpperInvariant())))
        {
            if (!nodeById.TryGetValue(group.Key.ToNodeId, out var node)) continue;
            var port = node.EffectivePorts.FirstOrDefault(value => string.Equals(value.Id, group.First().ToPortId, StringComparison.OrdinalIgnoreCase));
            if (port is { AllowsMultipleConnections: false } && group.Count() > 1) issues.Add(new("port.multiple", $"Input port '{port.Id}' accepts only one connection.", node.Id));
        }

        if (issues.All(issue => !issue.Code.StartsWith("edge.", StringComparison.Ordinal) && !issue.Code.StartsWith("node.id.", StringComparison.Ordinal)))
        {
            var indegree = nodes.ToDictionary(node => node.Id, _ => 0);
            foreach (var edge in edges) if (indegree.ContainsKey(edge.ToNodeId)) indegree[edge.ToNodeId]++;
            var queue = new Queue<Guid>(nodes.Where(node => indegree[node.Id] == 0).Select(node => node.Id));
            var visited = 0;
            while (queue.Count > 0)
            {
                var id = queue.Dequeue(); visited++;
                foreach (var edge in edges.Where(edge => edge.FromNodeId == id))
                {
                    if (!indegree.ContainsKey(edge.ToNodeId)) continue;
                    if (--indegree[edge.ToNodeId] == 0) queue.Enqueue(edge.ToNodeId);
                }
            }
            if (visited != nodes.Count) issues.Add(new("graph.cycle", "Workflow graphs must not contain execution cycles."));
        }
        return issues;
    }

    public async Task<AutomationGraphRunResult> RunAsync(AutomationGraphDefinition graph, AutomationGraphRunMode mode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var startedAt = DateTimeOffset.UtcNow;
        var issues = Validate(graph);
        if (issues.Count > 0) return new(mode, false, startedAt, DateTimeOffset.UtcNow, issues, [], "Workflow validation failed before execution.");

        var order = TopologicalOrder(graph.Nodes, graph.Edges);
        var activatedEdges = new HashSet<Guid>();
        var outputs = new Dictionary<Guid, string?>();
        var typedOutputs = new Dictionary<(Guid NodeId, string PortId), AutomationGraphValue>();
        var trace = new List<AutomationGraphNodeTrace>(graph.Nodes.Count);
        foreach (var node in order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var incoming = graph.Edges.Where(edge => edge.ToNodeId == node.Id).ToArray();
            if (incoming.Length > 0 && !incoming.Any(edge => activatedEdges.Contains(edge.Id)))
            {
                trace.Add(new(node.Id, node.Category, AutomationGraphTraceStatus.Skipped, "Skipped because no incoming branch was selected."));
                continue;
            }
            var activeIncoming = incoming.Where(edge => activatedEdges.Contains(edge.Id)).ToArray();
            var inputs = activeIncoming.Where(edge => outputs.ContainsKey(edge.FromNodeId)).GroupBy(edge => edge.FromNodeId).ToDictionary(group => group.Key, group => outputs[group.Key]);
            var typedInputs = BuildTypedInputs(activeIncoming, typedOutputs, node);
            AutomationGraphNodeExecutionResult result;
            IReadOnlyDictionary<string, AutomationGraphValue> nodeTypedOutputs;
            var invalidTypedInput = typedInputs.Values.SelectMany(values => values)
                .FirstOrDefault(value => value.State == AutomationGraphValueState.Error);
            if (invalidTypedInput is not null)
            {
                result = new(false, invalidTypedInput.Message ?? "A graph input is invalid.");
                nodeTypedOutputs = new Dictionary<string, AutomationGraphValue>();
            }
            else
            {
                try
                {
                    result = await ExecuteNodeAsync(new(node, mode, inputs, typedInputs), cancellationToken).ConfigureAwait(false);
                    nodeTypedOutputs = result.Succeeded ? ResolveTypedOutputs(node, result) : new Dictionary<string, AutomationGraphValue>();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    result = new(false, ex.Message);
                    nodeTypedOutputs = new Dictionary<string, AutomationGraphValue>();
                }
            }
            if (result.Succeeded && result.Yield is not null)
            {
                // A non-durable run cannot honestly sleep through or silently skip a durable Wait/Approval.
                // Refuse loudly instead of pretending the node completed, and let the owning durable path
                // (ResumeAsync + a continuation store) persist the suspension.
                trace.Add(new(node.Id, node.Category, AutomationGraphTraceStatus.Failed,
                    $"{result.Message} This {result.Yield.Kind} requires the durable run path to be persisted.", result.Output, result.Branch,
                    new Dictionary<Guid, string?>(inputs), typedInputs));
                foreach (var remaining in order.Where(candidate => trace.All(item => item.NodeId != candidate.Id)))
                    trace.Add(new(remaining.Id, remaining.Category, AutomationGraphTraceStatus.Skipped, $"Not run because the graph yielded at node {node.Id}."));
                return new(mode, false, startedAt, DateTimeOffset.UtcNow, [], trace, result.Message);
            }
            if (!result.Succeeded)
            {
                trace.Add(new(node.Id, node.Category, AutomationGraphTraceStatus.Failed, result.Message, result.Output, result.Branch, new Dictionary<Guid, string?>(inputs), typedInputs, result.TypedOutputs));
                foreach (var remaining in order.Where(candidate => trace.All(item => item.NodeId != candidate.Id))) trace.Add(new(remaining.Id, remaining.Category, AutomationGraphTraceStatus.Skipped, $"Not run because workflow failed at node {node.Id}."));
                return new(mode, false, startedAt, DateTimeOffset.UtcNow, [], trace, result.Message);
            }
            outputs[node.Id] = result.Output;
            foreach (var pair in nodeTypedOutputs)
                typedOutputs[(node.Id, pair.Key)] = pair.Value;
            trace.Add(new(node.Id, node.Category, AutomationGraphTraceStatus.Succeeded, result.Message, result.Output, result.Branch, new Dictionary<Guid, string?>(inputs), typedInputs, nodeTypedOutputs));
            foreach (var edge in graph.Edges.Where(edge => edge.FromNodeId == node.Id))
                if (string.IsNullOrWhiteSpace(edge.Branch) || (!string.IsNullOrWhiteSpace(result.Branch) && string.Equals(edge.Branch, result.Branch, StringComparison.OrdinalIgnoreCase))) activatedEdges.Add(edge.Id);
        }
        return new(mode, true, startedAt, DateTimeOffset.UtcNow, [], trace);
    }

    /// <summary>Runs (or resumes) a graph from a detached, strictly versioned execution snapshot. The
    /// caller supplies the canonical graph identity (GraphID/pinned definition revision/schema/source
    /// digest) which is stamped into every yielded snapshot. Completed nodes and selected branches are
    /// never replayed; only the exact next frontier runs. When a node yields a durable Wait/Approval,
    /// the snapshot is returned so the owning runtime can persist it and release execution resources.
    /// This is the durable truth path — the legacy RunAsync refuses yields honestly.</summary>
    public async Task<AutomationGraphRunOutcome> ResumeAsync(
        AutomationGraphDefinition graph,
        AutomationGraphRunMode mode,
        AutomationGraphContinuation? from,
        Guid graphId,
        long pinnedRevision,
        int schemaVersion,
        string sourceDigest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var startedAt = from?.StartedAt ?? DateTimeOffset.UtcNow;
        var issues = Validate(graph);
        if (issues.Count > 0)
            return new AutomationGraphRunTerminal(new(mode, false, startedAt, DateTimeOffset.UtcNow, issues, [], "Workflow validation failed before execution."));

        var order = TopologicalOrder(graph.Nodes, graph.Edges);
        var activatedEdges = new HashSet<Guid>(from?.ActivatedEdges ?? []);
        var outputs = new Dictionary<Guid, string?>(from?.Outputs ?? new Dictionary<Guid, string?>());
        var typedOutputs = new Dictionary<(Guid NodeId, string PortId), AutomationGraphValue>();
        if (from is not null)
            foreach (var typed in from.TypedOutputs) typedOutputs[(typed.NodeId, typed.PortId)] = typed.Value;

        var finishedStates = new List<AutomationGraphNodeState>(from?.CompletedNodes ?? []);
        var finishedIds = new HashSet<Guid>(finishedStates.Select(state => state.NodeId));
        var trace = new List<AutomationGraphNodeTrace>(finishedStates.Count + graph.Nodes.Count);
        foreach (var state in finishedStates)
            trace.Add(new(state.NodeId, Category(graph, state.NodeId), state.Status, state.Message, state.Output, state.Branch));
        var consumedBudget = from?.ConsumedBudget ?? 0;

        foreach (var node in order)
        {
            if (finishedIds.Contains(node.Id)) continue;
            cancellationToken.ThrowIfCancellationRequested();

            var incoming = graph.Edges.Where(edge => edge.ToNodeId == node.Id).ToArray();
            if (incoming.Length > 0 && !incoming.Any(edge => activatedEdges.Contains(edge.Id)))
            {
                var skipped = new AutomationGraphNodeState(node.Id, AutomationGraphTraceStatus.Skipped, "Skipped because no incoming branch was selected.");
                finishedStates.Add(skipped);
                finishedIds.Add(node.Id);
                trace.Add(new(node.Id, node.Category, AutomationGraphTraceStatus.Skipped, skipped.Message));
                continue;
            }

            consumedBudget++;
            var activeIncoming = incoming.Where(edge => activatedEdges.Contains(edge.Id)).ToArray();
            var inputs = activeIncoming.Where(edge => outputs.ContainsKey(edge.FromNodeId)).GroupBy(edge => edge.FromNodeId).ToDictionary(group => group.Key, group => outputs[group.Key]);
            var typedInputs = BuildTypedInputs(activeIncoming, typedOutputs, node);

            AutomationGraphNodeExecutionResult result;
            IReadOnlyDictionary<string, AutomationGraphValue> nodeTypedOutputs;
            var invalidTypedInput = typedInputs.Values.SelectMany(values => values)
                .FirstOrDefault(value => value.State == AutomationGraphValueState.Error);
            if (invalidTypedInput is not null)
            {
                result = new(false, invalidTypedInput.Message ?? "A graph input is invalid.");
                nodeTypedOutputs = new Dictionary<string, AutomationGraphValue>();
            }
            else
            {
                try
                {
                    result = await ExecuteNodeAsync(new(node, mode, inputs, typedInputs), cancellationToken).ConfigureAwait(false);
                    nodeTypedOutputs = result.Succeeded ? ResolveTypedOutputs(node, result) : new Dictionary<string, AutomationGraphValue>();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    result = new(false, ex.Message);
                    nodeTypedOutputs = new Dictionary<string, AutomationGraphValue>();
                }
            }

            if (result.Succeeded && result.Yield is not null)
            {
                // Record the yield trace for inspection but do NOT add the node to completed state: it
                // is paused, and will be re-evaluated on resume. The snapshot carries the exact frontier.
                trace.Add(new(node.Id, node.Category, AutomationGraphTraceStatus.Succeeded, result.Message, result.Output, result.Branch,
                    new Dictionary<Guid, string?>(inputs), typedInputs, nodeTypedOutputs));
                applied continuation = BuildSnapshot(graph, mode, startedAt, order, finishedStates, trace,
                    activatedEdges, outputs, typedOutputs, consumedBudget, result.Yield,
                    graphId, pinnedRevision, schemaVersion, sourceDigest);
                return new AutomationGraphRunYield(continuation);
            }

            if (!result.Succeeded)
            {
                finishedStates.Add(new(node.Id, AutomationGraphTraceStatus.Failed, result.Message, result.Output, result.Branch));
                trace.Add(new(node.Id, node.Category, AutomationGraphTraceStatus.Failed, result.Message, result.Output, result.Branch, new Dictionary<Guid, string?>(inputs), typedInputs, result.TypedOutputs));
                foreach (var remaining in order.Where(candidate => !finishedIds.Contains(candidate.Id) && trace.All(item => item.NodeId != candidate.Id)))
                    trace.Add(new(remaining.Id, remaining.Category, AutomationGraphTraceStatus.Skipped, $"Not run because workflow failed at node {node.Id}."));
                return new AutomationGraphRunTerminal(new(mode, false, startedAt, DateTimeOffset.UtcNow, [], trace, result.Message));
            }

            outputs[node.Id] = result.Output;
            foreach (var pair in nodeTypedOutputs)
                typedOutputs[(node.Id, pair.Key)] = pair.Value;
            finishedStates.Add(new(node.Id, AutomationGraphTraceStatus.Succeeded, result.Message, result.Output, result.Branch));
            finishedIds.Add(node.Id);
            trace.Add(new(node.Id, node.Category, AutomationGraphTraceStatus.Succeeded, result.Message, result.Output, result.Branch,
                new Dictionary<Guid, string?>(inputs), typedInputs, nodeTypedOutputs));
            foreach (var edge in graph.Edges.Where(edge => edge.FromNodeId == node.Id))
                if (string.IsNullOrWhiteSpace(edge.Branch) || (!string.IsNullOrWhiteSpace(result.Branch) && string.Equals(edge.Branch, result.Branch, StringComparison.OrdinalIgnoreCase))) activatedEdges.Add(edge.Id);
        }

        return new AutomationGraphRunTerminal(new(mode, true, startedAt, DateTimeOffset.UtcNow, [], trace));
    }

    private static AutomationGraphContinuation BuildSnapshot(
        AutomationGraphDefinition graph,
        AutomationGraphRunMode mode,
        DateTimeOffset startedAt,
        IReadOnlyList<AutomationGraphNodeDefinition> order,
        IReadOnlyList<AutomationGraphNodeState> finishedStates,
        IReadOnlyList<AutomationGraphNodeTrace> trace,
        IReadOnlySet<Guid> activatedEdges,
        IReadOnlyDictionary<Guid, string?> outputs,
        IReadOnlyDictionary<(Guid NodeId, string PortId), AutomationGraphValue> typedOutputs,
        int consumedBudget,
        AutomationGraphYield pendingYield,
        Guid graphId, long pinnedRevision, int schemaVersion, string sourceDigest)
    {
        var finishedIds = finishedStates.Select(state => state.NodeId).ToHashSet();
        var frontier = order.Where(node => !finishedIds.Contains(node.Id)).Select(node => node.Id).ToArray();
        var typed = typedOutputs.Select(pair => new AutomationGraphTypedOutput(pair.Key.NodeId, pair.Key.PortId, pair.Value)).ToArray();
        var resumeAt = pendingYield.Kind == AutomationGraphYieldKind.Wait ? pendingYield.ResumeAt : null;
        return new AutomationGraphContinuation(
            AutomationGraphContinuation.CurrentVersion, graphId, pinnedRevision, schemaVersion, sourceDigest,
            startedAt, Array.AsReadOnly(finishedStates.ToArray()), Array.AsReadOnly(frontier),
            Array.AsReadOnly(activatedEdges.ToArray()), new Dictionary<Guid, string?>(outputs),
            Array.AsReadOnly(typed), consumedBudget, mode, resumeAt, pendingYield);
    }

    private static string Category(AutomationGraphDefinition graph, Guid nodeId) =>
        graph.Nodes.FirstOrDefault(node => node.Id == nodeId)?.Category ?? string.Empty;

    private static IReadOnlyDictionary<string, IReadOnlyList<AutomationGraphValue>> BuildTypedInputs(
        IReadOnlyList<AutomationGraphEdgeDefinition> incoming,
        IReadOnlyDictionary<(Guid NodeId, string PortId), AutomationGraphValue> outputs,
        AutomationGraphNodeDefinition node)
    {
        var values = new Dictionary<string, List<AutomationGraphValue>>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in incoming)
        {
            var targetPort = node.EffectivePorts.FirstOrDefault(port => string.Equals(port.Id, edge.ToPortId, StringComparison.OrdinalIgnoreCase));
            var declaredType = targetPort?.DataType ?? "any";
            if (!values.TryGetValue(edge.ToPortId, out var list)) values[edge.ToPortId] = list = [];
            if (outputs.TryGetValue((edge.FromNodeId, edge.FromPortId), out var value))
            {
                if (!string.Equals(declaredType, "any", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(value.DataType, "any", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(value.DataType, declaredType, StringComparison.OrdinalIgnoreCase))
                    list.Add(AutomationGraphValue.Failure(declaredType, "ConnectionTypeMismatch", $"Connected value type '{value.DataType}' does not match input type '{declaredType}'."));
                else
                    list.Add(value);
            }
            else
                list.Add(AutomationGraphValue.Unavailable(declaredType, "The connected node did not produce a value for this port."));
        }
        return values.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<AutomationGraphValue>)pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, AutomationGraphValue> ResolveTypedOutputs(
        AutomationGraphNodeDefinition node,
        AutomationGraphNodeExecutionResult result)
    {
        var declaredPorts = node.EffectivePorts.Where(port => port.Direction == AutomationGraphPortDirection.Output)
            .ToDictionary(port => port.Id, StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, AutomationGraphValue>(StringComparer.OrdinalIgnoreCase);
        if (result.TypedOutputs is not null)
        {
            foreach (var (portId, value) in result.TypedOutputs)
            {
                if (!declaredPorts.TryGetValue(portId, out var port))
                    throw new InvalidOperationException($"Node '{node.Id}' produced an undeclared output port '{portId}'.");
                if (!string.Equals(port.DataType, "any", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(port.DataType, value.DataType, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Node '{node.Id}' produced type '{value.DataType}' for port '{portId}', which declares '{port.DataType}'.");
                values.Add(portId, value);
            }
        }
        else if (result.Output is not null)
        {
            var legacyPort = declaredPorts.GetValueOrDefault("out");
            if (legacyPort is not null && !string.Equals(legacyPort.DataType, "flow", StringComparison.OrdinalIgnoreCase))
                values[legacyPort.Id] = AutomationGraphValue.FromString(result.Output) with { DataType = legacyPort.DataType == "any" ? "string" : legacyPort.DataType };
        }
        return values;
    }

    private async Task<AutomationGraphNodeExecutionResult> ExecuteNodeAsync(AutomationGraphNodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (IsTrigger(context.Node)) return new(true, context.Mode == AutomationGraphRunMode.Test ? "Trigger would start the workflow." : "Trigger started the workflow.");
        if (IsCondition(context.Node)) return EvaluateCondition(context.Node);
        return await _executors.First(value => value.CanExecute(context.Node)).ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private static AutomationGraphNodeExecutionResult EvaluateCondition(AutomationGraphNodeDefinition node)
    {
        var parameters = node.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (TryParameter(parameters, "expression", out var expression) || TryParameter(parameters, "value", out expression))
        {
            if (!bool.TryParse(expression, out var parsed)) return new(false, $"Condition value '{expression}' is not true or false.");
            var branch = parsed ? "true" : "false";
            return new(true, $"Condition evaluated to {branch}.", branch, branch);
        }
        if (!TryParameter(parameters, "left", out var left) || !TryParameter(parameters, "operator", out var op)) return new(false, "Condition nodes require either expression=true/false or left/operator/right parameters.");
        _ = TryParameter(parameters, "right", out var right);
        bool matched;
        switch (op.Trim().ToLowerInvariant())
        {
            case "equals": case "==": matched = string.Equals(left, right, StringComparison.OrdinalIgnoreCase); break;
            case "notequals": case "!=": matched = !string.Equals(left, right, StringComparison.OrdinalIgnoreCase); break;
            case "contains": matched = left.Contains(right ?? string.Empty, StringComparison.OrdinalIgnoreCase); break;
            case "notcontains": matched = !left.Contains(right ?? string.Empty, StringComparison.OrdinalIgnoreCase); break;
            case "exists": matched = !string.IsNullOrWhiteSpace(left); break;
            case "empty": matched = string.IsNullOrWhiteSpace(left); break;
            case "greaterthan": case ">": matched = TryNumber(left, out var leftNumber) && TryNumber(right, out var rightNumber) && leftNumber > rightNumber; break;
            case "lessthan": case "<": matched = TryNumber(left, out leftNumber) && TryNumber(right, out rightNumber) && leftNumber < rightNumber; break;
            default: return new(false, $"Unsupported condition operator '{op}'.");
        }
        var branchName = matched ? "true" : "false";
        return new(true, $"Condition evaluated to {branchName}.", branchName, branchName);
    }

    private static IReadOnlyList<AutomationGraphNodeDefinition> TopologicalOrder(IReadOnlyList<AutomationGraphNodeDefinition> nodes, IReadOnlyList<AutomationGraphEdgeDefinition> edges)
    {
        var sourceIndex = nodes.Select((node, index) => (node.Id, index)).ToDictionary(value => value.Id, value => value.index);
        var indegree = nodes.ToDictionary(node => node.Id, _ => 0);
        foreach (var edge in edges) indegree[edge.ToNodeId]++;
        var ready = nodes.Where(node => indegree[node.Id] == 0).OrderBy(node => sourceIndex[node.Id]).ToList();
        var result = new List<AutomationGraphNodeDefinition>(nodes.Count);
        while (ready.Count > 0)
        {
            var node = ready[0]; ready.RemoveAt(0); result.Add(node);
            foreach (var edge in edges.Where(edge => edge.FromNodeId == node.Id))
            {
                if (--indegree[edge.ToNodeId] != 0) continue;
                ready.Add(nodes.First(candidate => candidate.Id == edge.ToNodeId));
                ready.Sort((left, right) => sourceIndex[left.Id].CompareTo(sourceIndex[right.Id]));
            }
        }
        return result;
    }

    private static bool IsBuiltIn(AutomationGraphNodeDefinition node) => IsTrigger(node) || IsCondition(node);
    private static bool IsTrigger(AutomationGraphNodeDefinition node) => node.Category.Equals("Trigger", StringComparison.OrdinalIgnoreCase) || node.Category.Equals("Schedule", StringComparison.OrdinalIgnoreCase) || node.Category.Equals("ConditionWatch", StringComparison.OrdinalIgnoreCase) || node.Category.Equals("Condition Watch", StringComparison.OrdinalIgnoreCase);
    private static bool IsCondition(AutomationGraphNodeDefinition node) => node.Category.Equals("Condition", StringComparison.OrdinalIgnoreCase) || node.Category.Equals("Branch", StringComparison.OrdinalIgnoreCase);
    private static bool TryParameter(IReadOnlyDictionary<string, string> values, string key, out string value) { foreach (var pair in values) if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) { value = pair.Value ?? string.Empty; return true; } value = string.Empty; return false; }
    private static bool TryNumber(string? value, out double number) => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number);
}
