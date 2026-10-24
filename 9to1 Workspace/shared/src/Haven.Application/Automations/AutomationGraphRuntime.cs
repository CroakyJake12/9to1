using System.Buffers;
using System.Text;
using System.Text.Json;

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
    IReadOnlyDictionary<string, AutomationGraphValue>? TypedOutputs = null);
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
    private const int MaximumExactNumberTokenLength = 256;
    private const long MaximumExactNumberExponent = 1_000_000;
    private readonly record struct ExactJsonNumber(bool Negative, string Digits, long Exponent);

    private readonly IReadOnlyList<IAutomationGraphNodeExecutor> _executors = executors?.ToArray() ?? [];

    public IReadOnlyList<AutomationGraphValidationIssue> Validate(AutomationGraphDefinition graph) => ValidateCore(graph, true);

    /// <summary>Pure structure checks for inspecting saved definitions. This does not
    /// inspect permissions, live capability availability, executor support or run a node.</summary>
    public static IReadOnlyList<AutomationGraphValidationIssue> ValidateStructure(AutomationGraphDefinition graph) =>
        new AutomationGraphRunner([]).ValidateCore(graph, false);

    private IReadOnlyList<AutomationGraphValidationIssue> ValidateCore(AutomationGraphDefinition graph, bool checkExecutorSupport)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var issues = new List<AutomationGraphValidationIssue>();
        if (graph.Version != AutomationGraphDefinition.CurrentVersion)
        {
            issues.Add(new("graph.version.unsupported", $"Unsupported Automation graph version {graph.Version}."));
            return issues;
        }
        if (graph.Nodes is null || graph.Edges is null)
        {
            issues.Add(new("graph.shape", "The workflow graph must contain node and connection collections."));
            return issues;
        }
        var nodes = graph.Nodes;
        var edges = graph.Edges;
        if (nodes.Count == 0) issues.Add(new("graph.empty", "The workflow graph does not contain any nodes."));
        if (nodes.Any(node => node is null || node.Parameters is null || node.Metadata is null || node.EffectivePorts.Any(port => port is null)) ||
            edges.Any(edge => edge is null || edge.FromPortId is null || edge.ToPortId is null || edge.Metadata is null))
        {
            issues.Add(new("graph.shape", "The saved graph contains a missing node, port or connection value."));
            return issues;
        }

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
                if (!Enum.IsDefined(port.Direction)) issues.Add(new("port.direction.invalid", $"Port '{port.Id}' has an unsupported direction.", node.Id));
                if (string.IsNullOrWhiteSpace(port.DataType)) issues.Add(new("port.type.missing", $"Port '{port.Id}' must declare a data type.", node.Id));
            }
            if (checkExecutorSupport && !string.IsNullOrWhiteSpace(node.Category) && !IsBuiltIn(node) && !_executors.Any(executor => executor.CanExecute(node))) issues.Add(new("node.unsupported", $"No runtime executor is registered for node category '{node.Category}'.", node.Id));
        }

        var nodeById = nodes.Where(node => node.Id != Guid.Empty).GroupBy(node => node.Id).ToDictionary(group => group.Key, group => group.First());
        var edgeIds = new HashSet<Guid>();
        foreach (var edge in edges)
        {
            if (edge.Id == Guid.Empty) issues.Add(new("edge.id.empty", "Workflow connections must have stable IDs.", EdgeId: edge.Id));
            else if (!edgeIds.Add(edge.Id)) issues.Add(new("edge.id.duplicate", $"Workflow edge ID {edge.Id} is duplicated.", EdgeId: edge.Id));
            if (!nodeById.TryGetValue(edge.FromNodeId, out var from)) { issues.Add(new("edge.from.missing", "An edge starts at a node that does not exist.", EdgeId: edge.Id)); continue; }
            if (!nodeById.TryGetValue(edge.ToNodeId, out var to)) { issues.Add(new("edge.to.missing", "An edge ends at a node that does not exist.", EdgeId: edge.Id)); continue; }
            if (edge.FromNodeId == edge.ToNodeId) issues.Add(new("edge.self", "A workflow node cannot connect directly to itself.", edge.FromNodeId, edge.Id));

            var fromPort = from.EffectivePorts.FirstOrDefault(port => string.Equals(port.Id, edge.FromPortId, StringComparison.OrdinalIgnoreCase));
            var toPort = to.EffectivePorts.FirstOrDefault(port => string.Equals(port.Id, edge.ToPortId, StringComparison.OrdinalIgnoreCase));
            if (fromPort is null) issues.Add(new("edge.from-port.missing", $"Output port '{edge.FromPortId}' does not exist.", from.Id, edge.Id));
            else if (fromPort.Direction != AutomationGraphPortDirection.Output) issues.Add(new("edge.from-port.direction", $"Port '{edge.FromPortId}' is not an output port.", from.Id, edge.Id));
            if (IsCondition(from) && (string.Equals(edge.FromPortId, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(edge.FromPortId, "false", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(edge.Branch) &&
                !string.Equals(edge.Branch, edge.FromPortId, StringComparison.OrdinalIgnoreCase))
                issues.Add(new("edge.branch.mismatch", "Condition connection branch metadata must match its output port.", from.Id, edge.Id));
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

        foreach (var group in edges.GroupBy(edge => (edge.FromNodeId, Port: edge.FromPortId.ToUpperInvariant())))
        {
            if (!nodeById.TryGetValue(group.Key.FromNodeId, out var node)) continue;
            var port = node.EffectivePorts.FirstOrDefault(value => string.Equals(value.Id, group.First().FromPortId, StringComparison.OrdinalIgnoreCase));
            if (port is { AllowsMultipleConnections: false } && group.Count() > 1)
                issues.Add(new("port.multiple.output", $"Output port '{port.Id}' accepts only one connection.", node.Id));
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

    public Task<AutomationGraphRunResult> RunAsync(AutomationGraphDefinition graph, AutomationGraphRunMode mode,
        CancellationToken cancellationToken) => RunAsync(graph, mode, cancellationToken, null);

    /// <summary>Runs with caller-selected typed payloads for trigger output ports. Samples are
    /// validated against the graph before any node executor is invoked.</summary>
    public async Task<AutomationGraphRunResult> RunAsync(AutomationGraphDefinition graph, AutomationGraphRunMode mode,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>? triggerOutputs)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var startedAt = DateTimeOffset.UtcNow;
        var issues = ValidateStructure(graph).ToList();
        if (!Enum.IsDefined(mode))
            issues.Add(new("run.mode.invalid", "The requested Automation run mode is not supported; no node was executed."));
        issues.AddRange(ValidateTriggerOutputs(graph, triggerOutputs));
        if (issues.Count > 0) return new(mode, false, startedAt, DateTimeOffset.UtcNow, issues, [], "Workflow validation failed before execution.");
        issues = Validate(graph).ToList();
        if (issues.Count > 0) return new(mode, false, startedAt, DateTimeOffset.UtcNow, issues, [], "Workflow validation failed before execution.");

        var order = TopologicalOrder(graph.Nodes, graph.Edges);
        var activatedEdges = new HashSet<Guid>();
        var outputs = new Dictionary<Guid, string?>();
        var typedOutputs = new Dictionary<(Guid NodeId, string PortId), AutomationGraphValue>(new OutputPortKeyComparer());
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
                    if (result.Succeeded && triggerOutputs is not null && triggerOutputs.TryGetValue(node.Id, out var sampleOutputs))
                    {
                        var combined = nodeTypedOutputs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                        foreach (var pair in sampleOutputs) combined[pair.Key] = pair.Value;
                        nodeTypedOutputs = combined;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    result = new(false, ex.Message);
                    nodeTypedOutputs = new Dictionary<string, AutomationGraphValue>();
                }
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
            {
                var isConditionPort = IsCondition(node) && (string.Equals(edge.FromPortId, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(edge.FromPortId, "false", StringComparison.OrdinalIgnoreCase));
                var branch = isConditionPort ? edge.FromPortId : edge.Branch;
                if (string.IsNullOrWhiteSpace(branch) || (!string.IsNullOrWhiteSpace(result.Branch) &&
                    string.Equals(branch, result.Branch, StringComparison.OrdinalIgnoreCase))) activatedEdges.Add(edge.Id);
            }
        }
        return new(mode, true, startedAt, DateTimeOffset.UtcNow, [], trace);
    }

    private static IReadOnlyList<AutomationGraphValidationIssue> ValidateTriggerOutputs(
        AutomationGraphDefinition graph,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>? triggerOutputs)
    {
        if (triggerOutputs is null) return [];
        if (graph.Nodes is null || graph.Nodes.Any(node => node is null || node.EffectivePorts.Any(port => port is null))) return [];
        var issues = new List<AutomationGraphValidationIssue>();
        foreach (var (nodeId, outputs) in triggerOutputs)
        {
            var trigger = graph.Nodes.FirstOrDefault(node => node.Id == nodeId);
            if (trigger is null || !IsTrigger(trigger))
            {
                issues.Add(new("run.input.trigger.invalid", $"Sample outputs target missing or non-trigger node '{nodeId}'.", nodeId));
                continue;
            }
            if (outputs is null)
            {
                issues.Add(new("run.input.outputs.missing", "Trigger sample outputs must be provided as a typed port map.", nodeId));
                continue;
            }
            var samplePortIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (portId, value) in outputs)
            {
                if (string.IsNullOrWhiteSpace(portId))
                {
                    issues.Add(new("run.input.port.invalid", "Trigger sample output port IDs must be non-empty.", nodeId));
                    continue;
                }
                if (!samplePortIds.Add(portId))
                {
                    issues.Add(new("run.input.port.duplicate", $"Trigger sample repeats output port '{portId}' with different casing.", nodeId));
                    continue;
                }
                var port = trigger.EffectivePorts.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, portId, StringComparison.OrdinalIgnoreCase));
                if (port is null || port.Direction != AutomationGraphPortDirection.Output)
                {
                    issues.Add(new("run.input.port.invalid", $"'{portId}' is not a registered output port on trigger '{nodeId}'.", nodeId));
                    continue;
                }
                if (value is null || !Enum.IsDefined(value.State) || string.IsNullOrWhiteSpace(value.DataType))
                {
                    issues.Add(new("run.input.value.invalid", $"Sample value for trigger port '{portId}' is malformed.", nodeId));
                    continue;
                }
                if (!string.Equals(port.DataType, "any", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(port.DataType, value.DataType, StringComparison.OrdinalIgnoreCase))
                    issues.Add(new("run.input.type.mismatch", $"Sample type '{value.DataType}' does not match trigger output '{portId}' type '{port.DataType}'.", nodeId));
                if (!value.HasJsonValueCompatibleWithDataType())
                    issues.Add(new("run.input.value.shape", $"Sample value for trigger port '{portId}' violates its declared state or JSON shape for type '{value.DataType}'.", nodeId));
            }
        }
        return issues;
    }

    // Port validation and per-node output maps use ordinal case-insensitive IDs.
    // Keep the cross-node cache consistent without merging distinct NodeIDs.
    private sealed class OutputPortKeyComparer : IEqualityComparer<(Guid NodeId, string PortId)>
    {
        public bool Equals((Guid NodeId, string PortId) left, (Guid NodeId, string PortId) right) =>
            left.NodeId == right.NodeId && StringComparer.OrdinalIgnoreCase.Equals(left.PortId, right.PortId);

        public int GetHashCode((Guid NodeId, string PortId) key) =>
            HashCode.Combine(key.NodeId, key.PortId is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(key.PortId));
    }

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
                if (!value.HasJsonValueCompatibleWithDataType())
                    throw new InvalidOperationException($"Node '{node.Id}' produced a JSON value with an incompatible shape for port '{portId}' type '{port.DataType}'.");
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
        if (IsCondition(context.Node)) return EvaluateCondition(context);
        return await _executors.First(value => value.CanExecute(context.Node)).ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private static AutomationGraphNodeExecutionResult EvaluateCondition(AutomationGraphNodeExecutionContext context)
    {
        var node = context.Node;
        if (node.EffectivePorts.Any(port => port.Direction == AutomationGraphPortDirection.Input &&
                !string.Equals(port.DataType, "flow", StringComparison.OrdinalIgnoreCase)))
            return EvaluateTypedCondition(context);

        return EvaluateLegacyCondition(node);
    }

    private static AutomationGraphNodeExecutionResult EvaluateTypedCondition(AutomationGraphNodeExecutionContext context)
    {
        var node = context.Node;
        var inputs = context.TypedInputs ?? new Dictionary<string, IReadOnlyList<AutomationGraphValue>>(StringComparer.OrdinalIgnoreCase);
        if (!inputs.TryGetValue("left", out var leftValues) || leftValues.Count != 1 ||
            !inputs.TryGetValue("right", out var rightValues) || rightValues.Count != 1)
            return new(false, "Typed Condition requires exactly one left and one right input value.");

        var left = leftValues[0]; var right = rightValues[0];
        if (left.State is AutomationGraphValueState.Unavailable or AutomationGraphValueState.Redacted ||
            right.State is AutomationGraphValueState.Unavailable or AutomationGraphValueState.Redacted)
            return new(false, "Typed Condition cannot choose a branch from unavailable or redacted input data.");
        if (!string.Equals(left.DataType, right.DataType, StringComparison.OrdinalIgnoreCase))
            return new(false, $"Typed Condition cannot compare '{left.DataType}' with '{right.DataType}' without a registered conversion.");
        if (!TryParameter(node.Parameters, "operator", out var operatorText))
            return new(false, "Typed Condition requires an explicit comparison operator.");

        var operation = operatorText.Trim().ToLowerInvariant();
        bool matched;
        if (operation is "equals" or "==" or "notequals" or "!=")
        {
            var equality = CompareTypedValues(left, right);
            if (!equality.Succeeded) return new(false, equality.Error!);
            matched = operation is "equals" or "==" ? equality.Equal : !equality.Equal;
        }
        else if (operation is "greaterthan" or ">" or "lessthan" or "<")
        {
            if (left.State != AutomationGraphValueState.Value || right.State != AutomationGraphValueState.Value ||
                left.JsonValue is not { ValueKind: JsonValueKind.Number } numericLeftJson ||
                right.JsonValue is not { ValueKind: JsonValueKind.Number } numericRightJson)
                return new(false, "Typed Condition ordering requires two available finite numeric values.");

            int comparison;
            if (string.Equals(left.DataType, "integer", StringComparison.OrdinalIgnoreCase) &&
                numericLeftJson.TryGetInt64(out var leftInteger) && numericRightJson.TryGetInt64(out var rightInteger))
                comparison = leftInteger.CompareTo(rightInteger);
            else if (string.Equals(left.DataType, "number", StringComparison.OrdinalIgnoreCase) &&
                     TryCompareExactJsonNumbers(numericLeftJson, numericRightJson, out comparison))
            {
                // The raw decimal tokens are compared exactly within the explicit
                // parser bounds; binary floating-point is not used for branch choice.
            }
            else
                return new(false, $"Typed Condition ordering is unsupported for '{left.DataType}' values outside its exact token or exponent bounds.");

            matched = operation is "greaterthan" or ">" ? comparison > 0 : comparison < 0;
        }
        else if (operation is "contains" or "notcontains")
        {
            if (!string.Equals(left.DataType, "string", StringComparison.OrdinalIgnoreCase) ||
                left.State != AutomationGraphValueState.Value || right.State != AutomationGraphValueState.Value ||
                left.JsonValue is not { ValueKind: JsonValueKind.String } containsLeftJson ||
                right.JsonValue is not { ValueKind: JsonValueKind.String } containsRightJson)
                return new(false, "Typed Condition contains comparison requires two available registered string values.");
            var contains = containsLeftJson.GetString()!.Contains(containsRightJson.GetString()!, StringComparison.OrdinalIgnoreCase);
            matched = operation == "contains" ? contains : !contains;
        }
        else
        {
            return new(false, $"Typed Condition operator '{operatorText}' is unsupported for typed input values.");
        }

        var selectedBranch = matched ? "true" : "false";
        return new(true, $"Typed condition evaluated to {selectedBranch}.", selectedBranch, selectedBranch);
    }

    private static (bool Succeeded, bool Equal, string? Error) CompareTypedValues(
        AutomationGraphValue left, AutomationGraphValue right)
    {
        if (left.State == AutomationGraphValueState.Null || right.State == AutomationGraphValueState.Null)
            return (false, false, "Typed Condition cannot compare explicit Null until its comparison semantics are registered.");
        if (left.State != AutomationGraphValueState.Value || right.State != AutomationGraphValueState.Value ||
            left.JsonValue is not { } leftJson || right.JsonValue is not { } rightJson)
            return (false, false, "Typed Condition equality requires available values or explicit Null states.");

        if (string.Equals(left.DataType, "string", StringComparison.OrdinalIgnoreCase) &&
            leftJson.ValueKind == JsonValueKind.String && rightJson.ValueKind == JsonValueKind.String)
            return (true, string.Equals(leftJson.GetString(), rightJson.GetString(), StringComparison.OrdinalIgnoreCase), null);
        if (string.Equals(left.DataType, "boolean", StringComparison.OrdinalIgnoreCase) &&
            (leftJson.ValueKind is JsonValueKind.True or JsonValueKind.False) &&
            (rightJson.ValueKind is JsonValueKind.True or JsonValueKind.False))
            return (true, leftJson.GetBoolean() == rightJson.GetBoolean(), null);
        if (string.Equals(left.DataType, "integer", StringComparison.OrdinalIgnoreCase) &&
            leftJson.TryGetInt64(out var leftInteger) && rightJson.TryGetInt64(out var rightInteger))
            return (true, leftInteger == rightInteger, null);
        if (string.Equals(left.DataType, "number", StringComparison.OrdinalIgnoreCase))
            return TryCompareExactJsonNumbers(leftJson, rightJson, out var comparison)
                ? (true, comparison == 0, null)
                : (false, false, "Typed Condition number equality is unsupported outside its exact token or exponent bounds.");
        return (false, false, $"Typed Condition equality is not registered for data type '{left.DataType}'.");
    }

    /// <summary>Compares bounded JSON decimal tokens without converting through binary64
    /// or allocating arbitrary-precision integers. Out-of-bound tokens fail closed.</summary>
    private static bool TryCompareExactJsonNumbers(JsonElement left, JsonElement right, out int comparison)
    {
        comparison = 0;
        if (!TryParseExactJsonNumber(left, out var leftNumber) || !TryParseExactJsonNumber(right, out var rightNumber))
            return false;

        var leftZero = leftNumber.Digits == "0";
        var rightZero = rightNumber.Digits == "0";
        if (leftZero && rightZero) return true;
        if (leftZero) { comparison = rightNumber.Negative ? 1 : -1; return true; }
        if (rightZero) { comparison = leftNumber.Negative ? -1 : 1; return true; }
        if (leftNumber.Negative != rightNumber.Negative)
        {
            comparison = leftNumber.Negative ? -1 : 1;
            return true;
        }

        var leftMagnitude = leftNumber.Digits.Length + leftNumber.Exponent;
        var rightMagnitude = rightNumber.Digits.Length + rightNumber.Exponent;
        if (leftMagnitude != rightMagnitude)
            comparison = leftMagnitude < rightMagnitude ? -1 : 1;
        else
        {
            var length = Math.Max(leftNumber.Digits.Length, rightNumber.Digits.Length);
            for (var index = 0; index < length; index++)
            {
                var leftDigit = index < leftNumber.Digits.Length ? leftNumber.Digits[index] : '0';
                var rightDigit = index < rightNumber.Digits.Length ? rightNumber.Digits[index] : '0';
                if (leftDigit == rightDigit) continue;
                comparison = leftDigit < rightDigit ? -1 : 1;
                break;
            }
        }

        if (leftNumber.Negative) comparison = -comparison;
        return true;
    }

    private static bool TryParseExactJsonNumber(JsonElement value, out ExactJsonNumber number)
    {
        number = default;
        if (!TryGetBoundedNumberToken(value, out var text)) return false;

        var token = text.AsSpan();
        var negative = token[0] == '-';
        if (negative) token = token[1..];
        var exponentIndex = token.IndexOfAny('e', 'E');
        var mantissa = exponentIndex < 0 ? token : token[..exponentIndex];
        var explicitExponent = 0L;
        if (exponentIndex >= 0)
        {
            var exponentText = token[(exponentIndex + 1)..];
            if (!long.TryParse(exponentText, System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out explicitExponent) ||
                explicitExponent is < -MaximumExactNumberExponent or > MaximumExactNumberExponent)
                return false;
        }

        var pointIndex = mantissa.IndexOf('.');
        var integerDigits = pointIndex < 0 ? mantissa : mantissa[..pointIndex];
        var fractionDigits = pointIndex < 0 ? ReadOnlySpan<char>.Empty : mantissa[(pointIndex + 1)..];
        if (integerDigits.Length == 0 || (pointIndex >= 0 && fractionDigits.Length == 0)) return false;

        Span<char> digits = stackalloc char[MaximumExactNumberTokenLength];
        var count = 0;
        foreach (var digit in integerDigits)
        {
            if (digit is < '0' or > '9') return false;
            digits[count++] = digit;
        }
        foreach (var digit in fractionDigits)
        {
            if (digit is < '0' or > '9') return false;
            digits[count++] = digit;
        }

        var firstSignificant = 0;
        while (firstSignificant < count && digits[firstSignificant] == '0') firstSignificant++;
        if (firstSignificant == count)
        {
            number = new(false, "0", 0);
            return true;
        }

        var lastSignificant = count - 1;
        while (lastSignificant > firstSignificant && digits[lastSignificant] == '0') lastSignificant--;
        var trailingZeroCount = count - 1 - lastSignificant;
        var normalizedDigits = new string(digits[firstSignificant..(lastSignificant + 1)]);
        var normalizedExponent = explicitExponent - fractionDigits.Length + trailingZeroCount;
        number = new(negative, normalizedDigits, normalizedExponent);
        return true;
    }

    private static bool TryGetBoundedNumberToken(JsonElement value, out string text)
    {
        text = string.Empty;
        if (value.ValueKind != JsonValueKind.Number) return false;
        var buffer = new FixedJsonTokenBufferWriter(MaximumExactNumberTokenLength + 256);
        try
        {
            using (var writer = new Utf8JsonWriter(buffer))
            {
                value.WriteTo(writer);
                writer.Flush();
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (buffer.WrittenCount is 0 or > MaximumExactNumberTokenLength) return false;
        text = Encoding.UTF8.GetString(buffer.WrittenSpan);
        return true;
    }

    private sealed class FixedJsonTokenBufferWriter(int capacity) : IBufferWriter<byte>
    {
        private readonly byte[] _buffer = new byte[capacity];
        private int _written;
        public int WrittenCount => _written;
        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

        public void Advance(int count)
        {
            if (count < 0 || count > _buffer.Length - _written)
                throw new InvalidOperationException("The JSON number exceeds its bounded comparison buffer.");
            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_written);
        }

        private void EnsureCapacity(int sizeHint)
        {
            if (sizeHint < 0 || sizeHint > _buffer.Length - _written)
                throw new InvalidOperationException("The JSON number exceeds its bounded comparison buffer.");
        }
    }

    private static AutomationGraphNodeExecutionResult EvaluateLegacyCondition(AutomationGraphNodeDefinition node)
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
    private static bool IsTrigger(AutomationGraphNodeDefinition node) =>
        string.Equals(node.Category, "Trigger", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(node.Category, "Schedule", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(node.Category, "ConditionWatch", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(node.Category, "Condition Watch", StringComparison.OrdinalIgnoreCase);
    private static bool IsCondition(AutomationGraphNodeDefinition node) =>
        string.Equals(node.Category, "Condition", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(node.Category, "Branch", StringComparison.OrdinalIgnoreCase);
    private static bool TryParameter(IReadOnlyDictionary<string, string> values, string key, out string value) { foreach (var pair in values) if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) { value = pair.Value ?? string.Empty; return true; } value = string.Empty; return false; }
    private static bool TryNumber(string? value, out double number) => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number);
}
