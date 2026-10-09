using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace HavenOS.Images;

internal static class PictureVectorSegmentEditor
{
    internal static HomeProductivityObject Convert(HomeProductivityObject original, PictureVectorNodeTarget target,
        DocumentVectorSegmentKind kind)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var selected = PictureVectorNodeEditor.ReadNode(original, target.PathId, target.SubpathId, target.NodeId);
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(original.Content, original.ObjectId);
        var subpath = shape.Paths.Single(path => path.Id == target.PathId).Subpaths.Single(path => path.Id == target.SubpathId);
        var index = subpath.Nodes.FindIndex(node => node.Id == target.NodeId);
        if (index == 0) throw new InvalidOperationException("The first node has no incoming editable segment.");
        if (selected.IncomingSegment == kind) return original;
        var previous = subpath.Nodes[index - 1];
        var first = kind == DocumentVectorSegmentKind.Line ? null : selected.Control1 ??
            DocumentVectorPoint.Lerp(previous.Point, selected.Point, kind == DocumentVectorSegmentKind.Quadratic ? .5 : 1d / 3d);
        var second = kind == DocumentVectorSegmentKind.Cubic ? selected.Control2 ??
            DocumentVectorPoint.Lerp(previous.Point, selected.Point, 2d / 3d) : null;
        foreach (var point in new[] { first, second })
            if (point is not null && (!double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                throw new NotSupportedException("The retained endpoints cannot produce finite curve controls.");
        var editor = new DocumentVectorShapeEditor(DocumentVectorShapes.Clone(shape));
        editor.SetNodeSegment(target.NodeId, kind, first, second);
        var edited = editor.Shape.Paths.Single(path => path.Id == target.PathId)
            .Subpaths.Single(path => path.Id == target.SubpathId).Nodes.Single(node => node.Id == target.NodeId);
        var content = JsonNode.Parse(original.Content.GetRawText())!.AsObject();
        var nodeJson = content["Paths"]!.AsArray().Single(node => node!["Id"]!.GetValue<Guid>() == target.PathId)!["Subpaths"]!
            .AsArray().Single(node => node!["Id"]!.GetValue<Guid>() == target.SubpathId)!["Nodes"]!
            .AsArray().Single(node => node!["Id"]!.GetValue<Guid>() == target.NodeId)!.AsObject();
        nodeJson["IncomingSegment"] = JsonSerializer.SerializeToNode(edited.IncomingSegment);
        // Previously retained controls remain available through Line/Quadratic
        // conversion. They are inactive geometry there, not discarded metadata.
        // Only newly required missing controls are introduced; original point
        // values, precision and unsupported control properties remain exact.
        if (edited.Control1 is not null && nodeJson["Control1"] is null)
            nodeJson["Control1"] = JsonSerializer.SerializeToNode(edited.Control1);
        if (edited.Control2 is not null && nodeJson["Control2"] is null)
            nodeJson["Control2"] = JsonSerializer.SerializeToNode(edited.Control2);
        var updated = original with { Content = JsonSerializer.SerializeToElement(content) };
        _ = PictureVectorNodeEditor.ReadNode(updated, target.PathId, target.SubpathId, target.NodeId);
        _ = new HomeVectorShapeObjectHandler().Render(updated);
        return updated;
    }
}
