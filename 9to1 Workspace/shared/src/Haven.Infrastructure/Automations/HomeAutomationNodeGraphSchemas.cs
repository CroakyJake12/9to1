using System.Text.Json;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;

namespace Haven.Infrastructure.Automations;

/// <summary>
/// Production building block for the durable Automations Node Graph composition: the canonical schema
/// registry and owning bindings for the built-in automation node families (Trigger / Action / Wait /
/// Approval). It is the SAME registered shared schema the durable adapter and the canonical graph
/// repository use, so an automation graph that passes <see cref="NodeGraphSchemaRegistry.Validate"/> and
/// projects through <see cref="NodeGraphAutomationAdapter.Compile"/> is structurally consistent with the
/// shared registry. This never authorizes execution; live authority is the owner/action path.
/// </summary>
public static class HomeAutomationNodeGraphSchemas
{
    public const string ProfileId = "automations";

    public static AutomationNodeGraphSchemasResult Create()
    {
        var emptyConfig = JsonSerializer.SerializeToElement(new { });
        // The production schema ports are declared Multiple=true to match the canonical Automation node
        // projection (AutomationGraphNodeDefinition.DefaultPorts and the graph nodes in the shared registry),
        // so the SAME schema validates and compiles through NodeGraphAutomationAdapter without a port
        // cardinality mismatch.
        var flowIn = new GraphPortTemplate("in", GraphPortDirection.Input, "flow", true);
        var flowOut = new GraphPortTemplate("out", GraphPortDirection.Output, "flow", true);
        var types = new[]
        {
            Type("trigger", "Trigger", emptyConfig, flowIn, flowOut),
            Type("action", BuiltInAutomationNodeCategory.Action, emptyConfig, flowIn, flowOut),
            Type("wait", BuiltInAutomationNodeCategory.Wait, emptyConfig, flowIn, flowOut),
            Type("approval", BuiltInAutomationNodeCategory.Approval, emptyConfig, flowIn, flowOut)
        };
        var allowed = types.Select(type => type.TypeId).ToHashSet(StringComparer.Ordinal);
        var profile = new GraphCapabilityProfile(ProfileId, allowed, allowed);
        var schemas = new NodeGraphSchemaRegistry(types, [profile]);
        var bindings = types.Select(type => Binding(type.TypeId, type.CapabilityId, type.AppId, type.Version)).ToArray();
        return new AutomationNodeGraphSchemasResult(schemas, bindings);
    }

    private static GraphNodeType Type(string typeId, string category, JsonElement config,
        GraphPortTemplate input, GraphPortTemplate output) =>
        new(typeId, 1, "automations", typeId, config, [input, output]);

    private static AutomationNodeGraphBinding Binding(string typeId, string capabilityId, string appId, int version)
    {
        var category = BuiltInCategory(typeId);
        return new AutomationNodeGraphBinding(typeId, version, appId, capabilityId,
            static entityId => string.IsNullOrWhiteSpace(entityId) || MaintainedEntityKinds.Contains(entityId),
            node =>
            {
                var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (node.Configuration.ValueKind == JsonValueKind.Object && node.Configuration.TryGetProperty("parameters", out var raw) &&
                    raw.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in raw.EnumerateObject())
                        parameters[property.Name] = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString() ?? string.Empty
                            : property.Value.GetRawText();
                }
                if (category == BuiltInAutomationNodeCategory.Wait && !parameters.ContainsKey("until"))
                    parameters["until"] = "2000-01-01T00:00:00Z";
                return new AutomationGraphNodeDefinition(node.NodeId, category, null, null, parameters) { Title = category };
            });
    }

    private static string BuiltInCategory(string typeId) => typeId switch
    {
        "trigger" => "Trigger",
        "action" => BuiltInAutomationNodeCategory.Action,
        "wait" => BuiltInAutomationNodeCategory.Wait,
        "approval" => BuiltInAutomationNodeCategory.Approval,
        _ => BuiltInAutomationNodeCategory.Action
    };

    /// <summary>The maintained typed entity catalogue for the built-in automation node families. A graph
    /// node may carry no entity binding (unbound/blank) or one of these documented automation entity kinds;
    /// any other value is refused. This is a structural entity constraint — never the literal sentinel
    /// compare and never an execution grant.</summary>
    private static readonly IReadOnlySet<string> MaintainedEntityKinds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "automation.run", "automation.definition", "automation.graph",
            "trigger", "action", "wait", "approval"
        };
}

/// <summary>The registered shared automation node-graph schema and owning bindings for the production
/// composition. This is a structural catalogue, never an execution grant.</summary>
public sealed record AutomationNodeGraphSchemasResult(NodeGraphSchemaRegistry Schemas, AutomationNodeGraphBinding[] Bindings);
