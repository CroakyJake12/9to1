using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace HavenOS.Images;

/// <summary>Product adapter for the existing shared editor. Its detached working
/// model is never published wholesale: the exact original canonical payload,
/// including unsupported properties, remains the source of the owning update.</summary>
internal static class PictureVectorNodeEditor
{
    internal static DocumentVectorNode ReadNode(HomeProductivityObject original, Guid pathId, Guid subpathId, Guid nodeId)
    {
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(original.Content, original.ObjectId);
        var path = shape.Paths.SingleOrDefault(path => path.Id == pathId)
            ?? throw new InvalidOperationException("The selected vector path is unavailable.");
        var subpath = path.Subpaths.SingleOrDefault(subpath => subpath.Id == subpathId)
            ?? throw new InvalidOperationException("The selected vector subpath is unavailable.");
        var node = subpath.Nodes.SingleOrDefault(node => node.Id == nodeId)
            ?? throw new InvalidOperationException("The selected vector node is unavailable.");
        // Shared node editing uses an ID-only lookup. Canonical import permits
        // equal node IDs in different subpaths, so never edit an ambiguous ID.
        if (shape.Paths.SelectMany(path => path.Subpaths).SelectMany(subpath => subpath.Nodes).Count(value => value.Id == nodeId) != 1)
            throw new NotSupportedException("This node ID is reused in another subpath. Its original identity is preserved; node editing is unavailable.");
        return node;
    }

    internal static HomeProductivityObject Move(HomeProductivityObject original, PictureVectorNodeTarget target,
        int? controlIndex, double x, double y)
    {
        _ = ReadNode(original, target.PathId, target.SubpathId, target.NodeId);
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(original.Content, original.ObjectId);
        var subpath = shape.Paths.Single(path => path.Id == target.PathId).Subpaths.Single(value => value.Id == target.SubpathId);
        if (controlIndex is not null && subpath.Nodes[0].Id == target.NodeId)
            throw new InvalidOperationException("The first node has no incoming editable segment.");
        var editor = new DocumentVectorShapeEditor(DocumentVectorShapes.Clone(shape));
        if (controlIndex is { } control) editor.MoveControlPoint(target.NodeId, control, x, y);
        else editor.MoveNode(target.NodeId, x, y);
        var edited = editor.Shape.Paths.Single(path => path.Id == target.PathId)
            .Subpaths.Single(value => value.Id == target.SubpathId).Nodes.Single(node => node.Id == target.NodeId);
        var content = JsonNode.Parse(original.Content.GetRawText())!.AsObject();
        var node = content["Paths"]!.AsArray().Single(value => value!["Id"]!.GetValue<Guid>() == target.PathId)!["Subpaths"]!
            .AsArray().Single(value => value!["Id"]!.GetValue<Guid>() == target.SubpathId)!["Nodes"]!
            .AsArray().Single(value => value!["Id"]!.GetValue<Guid>() == target.NodeId)!.AsObject();
        if (controlIndex is { } index)
        {
            var point = index == 1 ? edited.Control1 : edited.Control2;
            var name = index == 1 ? "Control1" : "Control2";
            var retained = node[name] as JsonObject
                ?? throw new InvalidDataException("The original incoming segment has no retained control point.");
            retained["X"] = point!.X; retained["Y"] = point.Y;
        }
        else
        {
            node["X"] = edited.X; node["Y"] = edited.Y;
            // Point is an existing derived public property in the shared model.
            // Keep a retained projection coherent without inventing a new field.
            if (node["Point"] is JsonObject point) { point["X"] = edited.X; point["Y"] = edited.Y; }
        }
        var result = original with { Content = JsonSerializer.SerializeToElement(content) };
        _ = HomeVectorShapeObjectHandler.ReadCanonical(result.Content, original.ObjectId);
        _ = new HomeVectorShapeObjectHandler().Render(result);
        return result;
    }
}
