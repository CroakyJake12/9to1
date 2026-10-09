using System.Text.Json;
using Avalonia.Media.Imaging;
using HavenOS.Home.Core;

namespace HavenOS.Images;

public sealed partial class PictureEditorSession
{
    public PictureVectorNodeTarget CaptureVectorNode(PictureCompositionTarget vector, Guid pathId, Guid subpathId, Guid nodeId)
    {
        DemandComposition(vector, PictureCompositionTargetKind.Vector);
        var item = PictureCompositionAdapter.Read(Document).Pages[0].Objects.Single(item => item.ObjectId == vector.TargetId);
        _ = PictureVectorNodeEditor.ReadNode(PictureCompositionAdapter.ReadVector(item), pathId, subpathId, nodeId);
        return new(vector, pathId, subpathId, nodeId);
    }

    public IReadOnlyList<PictureVectorNodeTarget> CaptureVectorNodes(PictureCompositionTarget vector, Guid pathId)
    {
        DemandComposition(vector, PictureCompositionTargetKind.Vector);
        var item = PictureCompositionAdapter.Read(Document).Pages[0].Objects.Single(item => item.ObjectId == vector.TargetId);
        var original = PictureCompositionAdapter.ReadVector(item);
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(original.Content, original.ObjectId);
        var path = shape.Paths.SingleOrDefault(path => path.Id == pathId)
            ?? throw new InvalidOperationException("The selected vector path is unavailable.");
        var counts = shape.Paths.SelectMany(path => path.Subpaths).SelectMany(subpath => subpath.Nodes)
            .GroupBy(node => node.Id).ToDictionary(group => group.Key, group => group.Count());
        return path.Subpaths.SelectMany(subpath => subpath.Nodes.Where(node => counts[node.Id] == 1)
            .Select(node => new PictureVectorNodeTarget(vector, pathId, subpath.Id, node.Id))).ToArray();
    }

    public Bitmap MoveVectorNode(PictureVectorNodeTarget target, double x, double y) =>
        EditVectorNode(target, null, x, y);

    public Bitmap MoveVectorControlPoint(PictureVectorNodeTarget target, int controlIndex, double x, double y) =>
        EditVectorNode(target, controlIndex, x, y);

    private Bitmap EditVectorNode(PictureVectorNodeTarget target, int? controlIndex, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(target);
        DemandComposition(target.Vector, PictureCompositionTargetKind.Vector);
        _ = CaptureVectorNode(target.Vector, target.PathId, target.SubpathId, target.NodeId);
        return ApplyComposition(controlIndex is null ? "Move shared vector node" : "Move shared vector control point", document =>
            PictureCompositionAdapter.Mutate(document, target.Vector.GraphRevision, (owner, pageId) =>
            {
                var item = owner.GetArtifactSnapshot().Pages.Single(page => page.PageId == pageId).Objects.Single(item => item.ObjectId == target.Vector.TargetId);
                var original = PictureCompositionAdapter.ReadVector(item);
                var updated = PictureVectorNodeEditor.Move(original, target, controlIndex, x, y);
                return owner.UpdateSharedObject(PictureCompositionAdapter.Request(owner), pageId,
                    item with { SharedPayload = JsonSerializer.SerializeToElement(updated) });
            }));
    }
}
