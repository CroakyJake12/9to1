using Avalonia.Controls;
using Haven.Core;

namespace HavenOS.Images;

public sealed partial class MainWindow
{
    private void ConvertSelectedVectorSegment(DocumentVectorSegmentKind kind)
    {
        var target = SelectedVectorNode();
        // This UI’s numeric controls must be able to show any newly introduced
        // controls. Refuse unsupported retained coordinates before publication.
        var owner = _session!; var item = PictureCompositionAdapter.Read(owner.Document).Pages[0].Objects.Single(item => item.ObjectId == target.Vector.TargetId);
        var candidate = PictureVectorSegmentEditor.Convert(PictureCompositionAdapter.ReadVector(item), target, kind);
        var candidateNode = PictureVectorNodeEditor.ReadNode(candidate, target.PathId, target.SubpathId, target.NodeId);
        if (!CanDisplayNode(candidateNode)) throw new NotSupportedException("The resulting controls exceed this editor’s numeric range.");
        ChangeComposition("Convert path segment", sameOwner =>
        {
            var actual = sameOwner.ConvertVectorSegment(target, kind);
            RefreshUntouchedSegmentCoordinates(sameOwner, target);
            return actual;
        });
    }
    private void RefreshUntouchedSegmentCoordinates(PictureEditorSession owner, PictureVectorNodeTarget target)
    {
        var item = PictureCompositionAdapter.Read(owner.Document).Pages[0].Objects.Single(item => item.ObjectId == target.Vector.TargetId);
        var node = PictureVectorNodeEditor.ReadNode(PictureCompositionAdapter.ReadVector(item), target.PathId, target.SubpathId, target.NodeId);
        void DisplayUntouched(string name, double value)
        {
            var input = Find<NumericUpDown>(name);
            if (!_vectorCoordinateBaselines.TryGetValue(name, out var original) || input.Value != original) return;
            input.Value = (decimal)value; _vectorCoordinateBaselines[name] = input.Value;
        }
        // Pending position/control edits stay entered. Unchanged fields show
        // actual new defaults and retain exact canonical doubles on later edit.
        DisplayUntouched("VectorNodeXBox", node.X); DisplayUntouched("VectorNodeYBox", node.Y);
        DisplayUntouched("VectorControl1XBox", node.Control1?.X ?? 0); DisplayUntouched("VectorControl1YBox", node.Control1?.Y ?? 0);
        DisplayUntouched("VectorControl2XBox", node.Control2?.X ?? 0); DisplayUntouched("VectorControl2YBox", node.Control2?.Y ?? 0);
    }
}
