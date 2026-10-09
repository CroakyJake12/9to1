using Haven.Core;

namespace HavenOS.Images;

public sealed partial class MainWindow
{
    private PictureVectorNodeTarget? _originalVectorSubpathTarget;
    private bool IsCurrentVectorSubpathTarget(PictureVectorNodeTarget? target) => target is not null && _session is not null &&
        ReferenceEquals(target.Vector.Owner, _session) && target.Vector.DocumentId == _session.Document.DocumentId &&
        target.Vector.DocumentRevision == _session.Document.Revision && _session.Document.CompositionState is not null &&
        target.Vector.GraphRevision == PictureCompositionAdapter.Read(_session.Document).RevisionId &&
        target.Vector.TargetId == _selectedVector?.TargetId && target.PathId == _selectedVectorPath &&
        target.SubpathId == _selectedVectorSubpath && target.NodeId == _selectedVectorNode;
    private void RefreshVectorSubpathFields(DocumentVectorSubpath? selected, bool canChange)
    {
        if (selected is null || _selectedVectorNode is null) _originalVectorSubpathTarget = null;
        else if (!IsCurrentVectorSubpathTarget(_originalVectorSubpathTarget)) _originalVectorSubpathTarget = SelectedVectorNode();
        _layout.Set("OriginalVectorSubpathTarget", _originalVectorSubpathTarget);
        _layout.Set("CanCloseVectorSubpath", canChange && selected is { Closed: false });
        _layout.Set("CanOpenVectorSubpath", canChange && selected is { Closed: true });
        _layout.Set("VectorSubpathLabel", selected is null ? "Select a path anchor to change its subpath."
            : selected.Closed ? "Closed subpath · the last anchor connects back to the first."
            : "Open subpath · the stroke ends at the first and last anchors. A fill can still close its interior.");
    }
    private void SetSelectedVectorSubpathClosed(object? parameter, bool closed)
    {
        if (parameter is not PictureVectorNodeTarget target || !ReferenceEquals(target, _originalVectorSubpathTarget) || !IsCurrentVectorSubpathTarget(target))
            throw new InvalidOperationException("Select the SAME current path and subpath again.");
        ChangeComposition(closed ? "Close subpath" : "Open subpath", owner => owner.SetVectorSubpathClosed(target, closed));
    }
}
