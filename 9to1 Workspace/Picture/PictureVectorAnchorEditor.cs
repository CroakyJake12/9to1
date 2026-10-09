using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace HavenOS.Images;

internal static class PictureVectorAnchorEditor
{
    internal static HomeProductivityObject Add(HomeProductivityObject original, PictureVectorNodeTarget target,
        double x, double y, out Guid addedId)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x), "Enter finite point coordinates.");
        _ = PictureVectorNodeEditor.ReadNode(original, target.PathId, target.SubpathId, target.NodeId);
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(original.Content, original.ObjectId);
        // Shared AddNode addresses a subpath by ID. Preserve an ambiguous
        // imported ID rather than insert into a different retained path.
        if (shape.Paths.SelectMany(path => path.Subpaths).Count(subpath => subpath.Id == target.SubpathId) != 1)
            throw new NotSupportedException("This subpath ID is reused in another path. Adding an anchor is unavailable.");
        var selected = shape.Paths.Single(path => path.Id == target.PathId).Subpaths.Single(subpath => subpath.Id == target.SubpathId);
        var index = selected.Nodes.FindIndex(node => node.Id == target.NodeId) + 1;
        var used = shape.Paths.SelectMany(path => path.Subpaths).SelectMany(subpath => subpath.Nodes).Select(node => node.Id).ToHashSet();
        var id = Guid.NewGuid();
        if (used.Contains(id)) throw new InvalidOperationException("A unique new anchor identity could not be issued.");
        var editor = new DocumentVectorShapeEditor(DocumentVectorShapes.Clone(shape));
        var issued = editor.AddNode(target.SubpathId, new DocumentVectorNode { Id = id, X = x, Y = y,
            IncomingSegment = DocumentVectorSegmentKind.Line }, index);
        if (issued != id) throw new InvalidOperationException("The original shared editor did not retain the issued anchor identity.");
        var added = editor.Shape.Paths.Single(path => path.Id == target.PathId).Subpaths.Single(subpath => subpath.Id == target.SubpathId)
            .Nodes.Single(node => node.Id == id);
        var content = JsonNode.Parse(original.Content.GetRawText())!.AsObject();
        OriginalNodes(content, target).Insert(index, JsonSerializer.SerializeToNode(added));
        var updated = original with { Content = JsonSerializer.SerializeToElement(content) };
        _ = PictureVectorNodeEditor.ReadNode(updated, target.PathId, target.SubpathId, id);
        _ = new HomeVectorShapeObjectHandler().Render(updated); addedId = id; return updated;
    }
    internal static HomeProductivityObject Delete(HomeProductivityObject original, PictureVectorNodeTarget target)
    {
        _ = PictureVectorNodeEditor.ReadNode(original, target.PathId, target.SubpathId, target.NodeId);
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(original.Content, original.ObjectId);
        var subpath = shape.Paths.Single(path => path.Id == target.PathId).Subpaths.Single(subpath => subpath.Id == target.SubpathId);
        if (subpath.Nodes.Count <= 2) throw new InvalidOperationException("A path must keep at least two anchors.");
        var editor = new DocumentVectorShapeEditor(DocumentVectorShapes.Clone(shape));
        if (!editor.DeleteNode(target.NodeId)) throw new InvalidOperationException("The original selected anchor could not be removed.");
        var content = JsonNode.Parse(original.Content.GetRawText())!.AsObject(); var nodes = OriginalNodes(content, target);
        var selected = nodes.Single(node => node!["Id"]!.GetValue<Guid>() == target.NodeId); nodes.Remove(selected);
        // Remaining original controls/unknown JSON are retained, including an
        // inactive incoming segment when the next node becomes the first one.
        var updated = original with { Content = JsonSerializer.SerializeToElement(content) };
        _ = HomeVectorShapeObjectHandler.ReadCanonical(updated.Content, updated.ObjectId);
        _ = new HomeVectorShapeObjectHandler().Render(updated); return updated;
    }
    private static JsonArray OriginalNodes(JsonObject content, PictureVectorNodeTarget target) =>
        content["Paths"]!.AsArray().Single(path => path!["Id"]!.GetValue<Guid>() == target.PathId)!["Subpaths"]!
            .AsArray().Single(subpath => subpath!["Id"]!.GetValue<Guid>() == target.SubpathId)!["Nodes"]!.AsArray();
}
