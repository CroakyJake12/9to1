using System.Text.Json;
using Haven.Application.NodeGraph;

namespace Haven.Application.Automations;

/// <summary>Owning compiled registrations for deterministic flow/value nodes. These are
/// schemas and projections into the existing engine, not permissions or another runtime.</summary>
public static class CanonicalAutomationCoreNodes
{
    public const string ProfileId = "automations.core.v1";
    public const string EmptyProfileId = "automations.empty-draft.v1";
    public const string ManualType = "automations.trigger.manual";
    public const string EmitType = "automations.value.emit";
    public const string FlowCapability = "automations.flow";
    public const string ValueCapability = "automations.value";
    private static JsonElement Json(string text)
    { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
    public static IReadOnlyList<GraphNodeType> Types() =>
    [
        new(ManualType, 1, "automations", FlowCapability,
            Json("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"),
            [new("out", GraphPortDirection.Output, "flow", true)]),
        new(EmitType, 1, "automations", ValueCapability,
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["type"] = "object", ["properties"] = new Dictionary<string, object?>
                {
                    ["value"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 4000,
                        ["pattern"] = @"^(?!\{\{input\}\}$)[\s\S]*$" }
                },
                ["required"] = new[] { "value" }, ["additionalProperties"] = false
            }),
            [new("in", GraphPortDirection.Input, "flow"), new("out", GraphPortDirection.Output, "flow", true)])
    ];
    public static NodeGraphSchemaRegistry CreateRegistry() => new(Types().Concat(SpaceUserMessageGraph.Types()),
    [
        new(EmptyProfileId, new HashSet<string>(), new HashSet<string>()),
        new(ProfileId, new HashSet<string>([ManualType, EmitType], StringComparer.Ordinal),
            new HashSet<string>([FlowCapability, ValueCapability], StringComparer.Ordinal)),
        SpaceUserMessageGraph.Profile()
    ]);
    public static bool IsRegistered(NodeGraphSchemaRegistry schemas) =>
        JsonSerializer.Serialize(schemas.ListNodeTypes(ProfileId).OrderBy(value => value.TypeId, StringComparer.Ordinal)) ==
        JsonSerializer.Serialize(Types().OrderBy(value => value.TypeId, StringComparer.Ordinal));
    public static GraphNode CreateNode(NodeGraphSchemaRegistry schemas, string typeId)
    {
        var type = schemas.ListNodeTypes(ProfileId).Single(value => value.TypeId == typeId && value.Version == 1);
        return new(Guid.NewGuid(), type.TypeId, type.Version, type.AppId, type.CapabilityId, null,
            typeId == EmitType ? Json("{\"value\":\"\"}") : Json("{}"),
            Array.AsReadOnly(type.Ports.Select(port => new GraphPort(Guid.NewGuid(), port.Key, port.Direction,
                port.DataType, port.Multiple)).ToArray()));
    }
    public static IReadOnlyList<AutomationNodeGraphBinding> Bindings() =>
    [
        new(ManualType, 1, "automations", FlowCapability, entity => entity is null,
            node => Project(node, "Trigger", null, new Dictionary<string, string>())),
        new(EmitType, 1, "automations", ValueCapability, entity => entity is null,
            node => Project(node, BuiltInAutomationNodeCategory.Action, "emit",
                new Dictionary<string, string> { ["action"] = "emit", ["value"] = node.Configuration.GetProperty("value").GetString()! }))
    ];
    private static AutomationGraphNodeDefinition Project(GraphNode node, string category, string? action,
        IReadOnlyDictionary<string, string> parameters) => new(node.NodeId, category, null, action, parameters)
    {
        Ports = Array.AsReadOnly(node.Ports.Select(port => new AutomationGraphPortDefinition(port.Key, port.Key,
            port.Direction == GraphPortDirection.Input ? AutomationGraphPortDirection.Input : AutomationGraphPortDirection.Output,
            port.DataType, port.Multiple)).ToArray())
    };
}
