using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Images;

internal static class PictureVectorSubpathEditor
{
    internal static HomeProductivityObject SetClosed(HomeProductivityObject original, PictureVectorNodeTarget target, bool closed)
    {
        _ = PictureVectorNodeEditor.ReadNode(original, target.PathId, target.SubpathId, target.NodeId);
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(original.Content, original.ObjectId);
        var selected = shape.Paths.Single(path => path.Id == target.PathId).Subpaths.Single(subpath => subpath.Id == target.SubpathId);
        if (selected.Closed == closed) return original;
        var editor = new DocumentVectorShapeEditor(DocumentVectorShapes.Clone(shape));
        editor.SetSubpathClosed(target.PathId, target.SubpathId, closed);
        var actual = editor.Shape.Paths.Single(path => path.Id == target.PathId).Subpaths.Single(subpath => subpath.Id == target.SubpathId);
        var content = JsonNode.Parse(original.Content.GetRawText())!;
        content["Paths"]!.AsArray().Single(path => path!["Id"]!.GetValue<Guid>() == target.PathId)!["Subpaths"]!.AsArray()
            .Single(subpath => subpath!["Id"]!.GetValue<Guid>() == target.SubpathId)!["Closed"] = actual.Closed;
        var updated = original with { Content = JsonSerializer.SerializeToElement(content) };
        _ = HomeVectorShapeObjectHandler.ReadCanonical(updated.Content, updated.ObjectId); _ = new HomeVectorShapeObjectHandler().Render(updated);
        return updated;
    }
}
