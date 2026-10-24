using Haven.Core;

namespace Haven.Application.Automations;

public interface IAutomationGraphAiEditor
{
    Task<AutomationGraphAiEditResult> ProposeEditAsync(
        AutomationGraphDefinition current,
        string instruction,
        CancellationToken cancellationToken);
}

public sealed record AutomationGraphAiEditResult(
    bool Succeeded,
    string Status,
    string? ModelName,
    AutomationGraphDefinition? Graph);

/// <summary>Uses Haven's provider model runtime to propose typed Automation graph changes.</summary>
public sealed class AutomationGraphAiEditor(IProviderModelClient models) : IAutomationGraphAiEditor
{
    private static readonly HashSet<string> BuiltInCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Trigger", "Schedule", "ConditionWatch", "Condition Watch", "Condition", "Branch",
        BuiltInAutomationNodeCategory.App, BuiltInAutomationNodeCategory.File,
        BuiltInAutomationNodeCategory.Action, DeviceAutomationNodeCategory.Key
    };

    public async Task<AutomationGraphAiEditResult> ProposeEditAsync(
        AutomationGraphDefinition current,
        string instruction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (string.IsNullOrWhiteSpace(instruction))
            return Fail("Describe the graph change you want 9to1 to make.");

        string currentJson;
        try { currentJson = AutomationGraphCodec.Serialize(current); }
        catch (InvalidOperationException exception)
        {
            return Fail("The current graph is not a supported editable snapshot; it was preserved without requesting a model edit. " + exception.Message);
        }
        var currentGraphIssue = ValidateCurrentGraph(current);
        if (currentGraphIssue is not null)
            return Fail("The current graph is not a valid editable snapshot; it was preserved without requesting a model edit. " + currentGraphIssue);

        ModelDescriptor? model;
        try
        {
            var available = await models.GetModelsAsync(cancellationToken).ConfigureAwait(false);
            model = available.FirstOrDefault(item => item.Supports(ToolCapability.Text));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            return Fail("The graph edit could not discover an available model: " + exception.Message);
        }
        if (model is null) return Fail("No text-capable model is currently available.");

        var prompt = $$"""
            Edit this Haven Automation graph according to the user's instruction.

            User instruction:
            {{instruction.Trim()}}

            Current graph JSON:
            {{currentJson}}

            Return one complete AutomationGraphDefinition JSON object only.
            Required version: {{AutomationGraphDefinition.CurrentVersion}}.
            Built-in categories available for new nodes: Trigger, Schedule, ConditionWatch, Condition, Branch, App, File, Action, DEVICE.
            Existing non-built-in nodes are opaque custom data. Preserve each such node and every connection touching it exactly, including IDs, category, configuration, ports, metadata, layout and branch data. Do not add, remove, or modify opaque custom nodes or their connections. If the request requires such a change, return the unchanged graph; the caller will report that the registered typed schema is unavailable here.
            Preserve existing node and edge IDs unless the corresponding object is intentionally removed.
            New nodes and edges need non-empty UUIDs.
            Use flow ports. Trigger/Schedule/ConditionWatch nodes have output port "out".
            Condition/Branch nodes have input "in" and outputs "true" and "false".
            Other nodes have input "in" and output "out".
            Keep positions finite, width >= 120, height >= 80.
            Do not invent DEVICE credentials, provider IDs, target IDs, or permission grants.
            For an existing DEVICE node, preserve its DeviceTarget and ActionKey unless the user explicitly asks to remove the node.
            App parameters: action=launch and name.
            File parameters: operation=read with workspaceRoot/path, or operation=search with workspaceRoot/pattern.
            Action parameters: action=emit with value, or action=delay with milliseconds.
            Condition/Branch parameters should use expression=true/false unless the user supplied another supported comparison.
            Do not include markdown fences or commentary.
            """;

        try
        {
            var response = await models.CompleteAsync(
                new OllamaChatRequest(
                    model.Name,
                    [new OllamaMessage("user", prompt)],
                    EffortLevel.Medium,
                    "You are 9to1 Automations' typed graph editor. Return strict JSON only. Never fabricate provider credentials or permission grants."),
                cancellationToken).ConfigureAwait(false);

            if (!TryParse(response, out var proposed))
                return Fail("The model did not return a readable Automation graph.", model.Name);

            var opaqueIssue = ValidateOpaqueCustomChanges(current, proposed);
            if (opaqueIssue is not null) return Fail(opaqueIssue, model.Name);
            if (current.Nodes.Any(node => !IsBuiltInCategory(node.Category)) && SameGraphContent(current, proposed))
                return Fail("No supported edit was produced. Changes to opaque custom nodes require their registered typed schema; the original graph was preserved.", model.Name);

            var normalized = Normalize(proposed, current);

            var validation = new AutomationGraphRunner([new StructuralExecutor()]).Validate(normalized);
            if (validation.Count > 0)
                return Fail("The proposed graph was rejected: " + validation[0].Message, model.Name);

            var status = current.Nodes.Any(node => !IsBuiltInCategory(node.Category))
                ? $"9to1 proposed a structurally valid edit using {model.Name}. Existing custom nodes were preserved as opaque; their registered schemas, capabilities and permissions were not validated."
                : $"9to1 proposed a validated typed graph edit using {model.Name}.";
            return new(true, status, model.Name, normalized);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            return Fail("The graph edit failed without changing the workflow: " + exception.Message, model.Name);
        }
    }

    private static bool TryParse(string value, out AutomationGraphDefinition graph)
    {
        graph = AutomationGraphDefinition.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        return start >= 0 && end > start && AutomationGraphCodec.TryDeserialize(value[start..(end + 1)], out graph);
    }

    private static string? ValidateCurrentGraph(AutomationGraphDefinition graph)
    {
        var structure = AutomationGraphRunner.ValidateStructure(graph);
        if (graph.Nodes.Count == 0 && graph.Edges.Count == 0 && structure.All(issue => issue.Code == "graph.empty"))
            return null;
        if (structure.Count != 0) return structure[0].Message;

        foreach (var node in graph.Nodes)
        {
            var actionIssue = BuiltInAutomationActionNodeExecutor.ValidateConfiguration(node).FirstOrDefault();
            if (actionIssue is not null) return actionIssue.Message;
            if (string.Equals(node.Category, DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase) && node.ToDevice() is null)
                return "A DEVICE node is missing its target or action.";
        }
        return null;
    }

    private static AutomationGraphDefinition Normalize(AutomationGraphDefinition proposed, AutomationGraphDefinition current)
    {
        var currentNodes = current.Nodes.ToDictionary(node => node.Id);
        var nodes = proposed.Nodes.Select(node =>
        {
            currentNodes.TryGetValue(node.Id, out var previous);
            if (previous is not null && !IsBuiltInCategory(previous.Category)) return previous;
            var category = node.Category?.Trim() ?? string.Empty;
            var ports = node.Ports is { Count: > 0 } ? node.Ports : PortsFor(category);
            var deviceTarget = category.Equals(DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase)
                ? previous?.DeviceTarget
                : node.DeviceTarget;
            var actionKey = category.Equals(DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase)
                ? previous?.ActionKey
                : node.ActionKey;
            return node with
            {
                Category = category,
                DeviceTarget = deviceTarget,
                ActionKey = actionKey,
                Parameters = node.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Metadata = node.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal),
                Ports = ports,
                X = double.IsFinite(node.X) ? node.X : previous?.X ?? 0,
                Y = double.IsFinite(node.Y) ? node.Y : previous?.Y ?? 0,
                Width = Math.Max(120, double.IsFinite(node.Width) ? node.Width : previous?.Width ?? 220),
                Height = Math.Max(80, double.IsFinite(node.Height) ? node.Height : previous?.Height ?? 118)
            };
        }).ToArray();
        var edges = proposed.Edges.Select(edge => edge with
        {
            Metadata = edge.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal)
        }).ToArray();
        return new(AutomationGraphDefinition.CurrentVersion, nodes, edges);
    }

    private static string? ValidateOpaqueCustomChanges(AutomationGraphDefinition current, AutomationGraphDefinition proposed)
    {
        var opaqueNodes = current.Nodes.Where(node => !IsBuiltInCategory(node.Category)).ToDictionary(node => node.Id);
        var proposedById = proposed.Nodes.ToDictionary(node => node.Id);
        foreach (var (id, original) in opaqueNodes)
        {
            if (!proposedById.TryGetValue(id, out var candidate))
                return $"Custom node '{original.Category}' was removed or omitted. This editor has no typed-schema adapter for deleting it; the original graph was preserved.";
            if (!SameOpaqueNode(original, candidate))
                return $"Custom node '{original.Category}' was modified. This editor has no typed-schema adapter for that mutation; the original graph was preserved.";
        }

        foreach (var node in proposed.Nodes)
            if (!IsBuiltInCategory(node.Category) && !opaqueNodes.ContainsKey(node.Id))
                return $"New custom node category '{node.Category}' cannot be resolved by this editor's typed-schema adapter; the graph was not changed.";

        var opaqueIds = opaqueNodes.Keys.ToHashSet();
        var originalConnections = current.Edges.Where(edge => opaqueIds.Contains(edge.FromNodeId) || opaqueIds.Contains(edge.ToNodeId))
            .ToDictionary(edge => edge.Id);
        foreach (var (id, original) in originalConnections)
        {
            var candidate = proposed.Edges.FirstOrDefault(edge => edge.Id == id);
            if (candidate is null || !SameOpaqueConnection(original, candidate))
                return "A connection touching an opaque custom node was removed or modified. This editor has no typed-schema adapter for that mutation; the original graph was preserved.";
        }
        foreach (var candidate in proposed.Edges.Where(edge => opaqueIds.Contains(edge.FromNodeId) || opaqueIds.Contains(edge.ToNodeId)))
            if (!originalConnections.TryGetValue(candidate.Id, out var original) || !SameOpaqueConnection(original, candidate))
                return "A new or modified connection touches an opaque custom node. This editor has no typed-schema adapter for that mutation; the original graph was preserved.";

        return null;
    }

    private static bool SameOpaqueNode(AutomationGraphNodeDefinition left, AutomationGraphNodeDefinition right) =>
        left.Id == right.Id && string.Equals(left.Category, right.Category, StringComparison.Ordinal) &&
        Equals(left.DeviceTarget, right.DeviceTarget) && string.Equals(left.ActionKey, right.ActionKey, StringComparison.Ordinal) &&
        SameStringMap(left.Parameters, right.Parameters) && SameStringMap(left.Metadata, right.Metadata) &&
        left.Ports is not null && right.Ports is not null && left.Ports.SequenceEqual(right.Ports) &&
        left.X.Equals(right.X) && left.Y.Equals(right.Y) && left.Width.Equals(right.Width) && left.Height.Equals(right.Height) &&
        string.Equals(left.Title, right.Title, StringComparison.Ordinal) && string.Equals(left.Subtitle, right.Subtitle, StringComparison.Ordinal);

    private static bool SameOpaqueConnection(AutomationGraphEdgeDefinition left, AutomationGraphEdgeDefinition right) =>
        left.Id == right.Id && left.FromNodeId == right.FromNodeId && left.ToNodeId == right.ToNodeId &&
        string.Equals(left.FromPortId, right.FromPortId, StringComparison.Ordinal) &&
        string.Equals(left.ToPortId, right.ToPortId, StringComparison.Ordinal) &&
        string.Equals(left.Branch, right.Branch, StringComparison.Ordinal) &&
        string.Equals(left.Label, right.Label, StringComparison.Ordinal) && SameStringMap(left.Metadata, right.Metadata);

    private static bool SameGraphContent(AutomationGraphDefinition left, AutomationGraphDefinition right)
    {
        if (left.Nodes.Count != right.Nodes.Count || left.Edges.Count != right.Edges.Count) return false;
        var rightNodes = right.Nodes.ToDictionary(node => node.Id);
        if (left.Nodes.Any(node => !rightNodes.TryGetValue(node.Id, out var other) || !SameOpaqueNode(node, other))) return false;
        var rightEdges = right.Edges.ToDictionary(edge => edge.Id);
        return left.Edges.All(edge => rightEdges.TryGetValue(edge.Id, out var other) && SameOpaqueConnection(edge, other));
    }

    private static bool SameStringMap(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string>? right)
    {
        if (left is null || right is null || left.Count != right.Count) return false;
        var rightByExactKey = right.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return left.All(pair => rightByExactKey.TryGetValue(pair.Key, out var value) &&
            string.Equals(pair.Value, value, StringComparison.Ordinal));
    }

    private static bool IsBuiltInCategory(string? category) => category is not null && BuiltInCategories.Contains(category);

    private static IReadOnlyList<AutomationGraphPortDefinition> PortsFor(string category)
    {
        if (category.Equals("Trigger", StringComparison.OrdinalIgnoreCase)
            || category.Equals("Schedule", StringComparison.OrdinalIgnoreCase)
            || category.Equals("ConditionWatch", StringComparison.OrdinalIgnoreCase)
            || category.Equals("Condition Watch", StringComparison.OrdinalIgnoreCase))
            return [new("out", "Out", AutomationGraphPortDirection.Output, "flow", true)];

        if (category.Equals("Condition", StringComparison.OrdinalIgnoreCase)
            || category.Equals("Branch", StringComparison.OrdinalIgnoreCase))
            return
            [
                new("in", "In", AutomationGraphPortDirection.Input, "flow", false),
                new("true", "True", AutomationGraphPortDirection.Output, "flow", true),
                new("false", "False", AutomationGraphPortDirection.Output, "flow", true)
            ];

        return
        [
            new("in", "In", AutomationGraphPortDirection.Input, "flow", false),
            new("out", "Out", AutomationGraphPortDirection.Output, "flow", true)
        ];
    }

    private static AutomationGraphAiEditResult Fail(string status, string? model = null) =>
        new(false, status, model, null);

    private sealed class StructuralExecutor : IAutomationGraphNodeExecutor
    {
        public bool CanExecute(AutomationGraphNodeDefinition node) => true;
        public Task<AutomationGraphNodeExecutionResult> ExecuteAsync(
            AutomationGraphNodeExecutionContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AutomationGraphNodeExecutionResult(true, "Structural validation only."));
    }
}
