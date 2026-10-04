using System.Text.Json;
using Haven.Core;
using Haven.Application.NodeGraph;

namespace Haven.Application;

public sealed record DataRelationshipGraphSnapshot(Guid WorkbookID, int WorkbookVersion, Guid WorkbookRevisionID,
    GraphDocument Graph, NodeGraphSchemaRegistry Schema);

/// <summary>A projection of the owning Data workbook, not a second schema or executable graph.
/// The returned registry validates drawing identities/types; it authorizes no runtime activation.</summary>
public static class DataRelationshipGraphProjection
{
    public const string ProfileID = "data.relationships.inspect";
    public const string CapabilityID = "data.schema.inspect";
    public static DataRelationshipGraphSnapshot Capture(DataWorkbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var captured = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook))!;
        captured.Normalize();
        var nodes = new List<GraphNode>(); var types = new List<GraphNodeType>();
        foreach (var table in captured.Tables.OrderBy(table => table.Id))
        {
            var ports = new List<GraphPort>();
            foreach (var field in table.RelationalSchema?.Fields ?? [])
                ports.Add(new(field.FieldID, $"field:{field.FieldID:D}", GraphPortDirection.Output,
                    Signature(table.RelationalSchema!, [field.FieldID]), true));
            foreach (var key in table.RelationalSchema?.Keys ?? [])
                ports.Add(new(key.KeyID, $"key:{key.KeyID:D}", GraphPortDirection.Input,
                    Signature(table.RelationalSchema!, key.FieldIDs), true));
            foreach (var relation in captured.Relationships.Where(relation => relation.SourceTableID == table.Id && relation.SourceFieldIDs.Count > 1))
                ports.Add(new(relation.RelationshipID, $"reference:{relation.RelationshipID:D}", GraphPortDirection.Output,
                    Signature(table.RelationalSchema!, relation.SourceFieldIDs), false));
            var typeID = $"data.relationship-table.{table.Id:D}";
            types.Add(new(typeID, 1, "data", CapabilityID, JsonSerializer.SerializeToElement(new
            {
                type = "object", required = new[] { "tableID" }, additionalProperties = false,
                properties = new { tableID = new { type = "string" } }
            }), ports.Select(port => new GraphPortTemplate(port.Key, port.Direction, port.DataType, port.Multiple)).ToArray()));
            nodes.Add(new(table.Id, typeID, 1, "data", CapabilityID, table.Id.ToString("D"),
                JsonSerializer.SerializeToElement(new { tableID = table.Id.ToString("D") }), ports));
        }
        var edges = captured.Relationships.Select(relation =>
            new GraphConnection(relation.RelationshipID, relation.SourceFieldIDs.Count == 1 ? relation.SourceFieldIDs[0] : relation.RelationshipID, relation.TargetKeyID)).ToArray();
        var graph = new GraphDocument(GraphDocument.CurrentSchemaVersion, captured.Id, Math.Max(1, captured.Version),
            ProfileID, GraphRevisionState.Draft, nodes, edges);
        var registry = new NodeGraphSchemaRegistry(types, [new(ProfileID,
            types.Select(type => type.TypeId).ToHashSet(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal) { CapabilityID })]);
        if (registry.Validate(graph).Count != 0) throw new InvalidDataException("The canonical Data relationship projection is invalid.");
        return new(captured.Id, captured.Version, captured.RevisionId, graph, registry);
    }
    private static string Signature(DataTableSchema schema, IReadOnlyList<Guid> fields) => "data.key:" +
        JsonSerializer.Serialize(fields.Select(id => schema.Fields.Single(field => field.FieldID == id).Type.ToString()).ToArray());
}
