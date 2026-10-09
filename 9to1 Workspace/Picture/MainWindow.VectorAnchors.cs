using Avalonia.Controls;
using Haven.Core;

namespace HavenOS.Images;

public sealed partial class MainWindow
{
    private bool _updatingVectorAnchor;
    private readonly HashSet<string> _vectorAnchorDraftFields = new(StringComparer.Ordinal);
    private PictureEditorSession? _vectorAnchorOwner;
    private PictureVectorNodeTarget? _vectorAnchorOriginal, _lastAddedVectorAnchor;
    private void ConfigureVectorAnchorInputs()
    {
        foreach (var name in new[] { "NewVectorAnchorXBox", "NewVectorAnchorYBox" })
        {
            var input = Find<NumericUpDown>(name); input.Minimum = decimal.MinValue; input.Maximum = decimal.MaxValue;
            input.FormatString = "G29"; input.ShowButtonSpinner = false;
            input.PropertyChanged += (_, change) =>
            {
                if (change.Property != NumericUpDown.ValueProperty || _updatingVectorAnchor) return;
                if (_vectorAnchorDraftFields.Count == 0)
                {
                    _vectorAnchorOwner = _session;
                    _vectorAnchorOriginal = _selectedVectorNode is null ? null : SelectedVectorNode();
                }
                _vectorAnchorDraftFields.Add(name); RefreshEditor();
            };
        }
    }
    private bool IsCurrentVectorAnchorDraft() => _vectorAnchorDraftFields.Count > 0 &&
        ReferenceEquals(_vectorAnchorOwner, _session) && IsCurrentVectorAnchorTarget(_vectorAnchorOriginal) &&
        _vectorAnchorOriginal!.NodeId == _selectedVectorNode && _vectorAnchorOriginal.SubpathId == _selectedVectorSubpath &&
        _vectorAnchorOriginal.PathId == _selectedVectorPath && _vectorAnchorOriginal.Vector.TargetId == _selectedVector?.TargetId;
    private bool IsCurrentVectorAnchorTarget(PictureVectorNodeTarget? target) => target is not null && _session is not null &&
        ReferenceEquals(target.Vector.Owner, _session) && target.Vector.DocumentId == _session.Document.DocumentId &&
        target.Vector.DocumentRevision == _session.Document.Revision && _session.Document.CompositionState is not null &&
        target.Vector.GraphRevision == PictureCompositionAdapter.Read(_session.Document).RevisionId;
    private void RefreshVectorAnchorFields(DocumentVectorNode? selected, bool canChangeNode, int count)
    {
        if (_vectorAnchorDraftFields.Count > 0 && (!ReferenceEquals(_vectorAnchorOwner, _session) ||
            _vectorAnchorOriginal?.Vector.DocumentId != _session?.Document.DocumentId)) ClearVectorAnchorDraft();
        if (_initialized && (_vectorAnchorDraftFields.Count == 0 || IsCurrentVectorAnchorDraft()))
        {
            _updatingVectorAnchor = true;
            try
            {
                if (!_vectorAnchorDraftFields.Contains("NewVectorAnchorXBox")) Find<NumericUpDown>("NewVectorAnchorXBox").Value = (decimal)(selected?.X ?? 0);
                if (!_vectorAnchorDraftFields.Contains("NewVectorAnchorYBox")) Find<NumericUpDown>("NewVectorAnchorYBox").Value = (decimal)(selected?.Y ?? 0);
            }
            finally { _updatingVectorAnchor = false; }
        }
        _layout.Set("CanAddVectorAnchor", canChangeNode && IsCurrentVectorAnchorDraft() &&
            Find<NumericUpDown>("NewVectorAnchorXBox").Value is not null && Find<NumericUpDown>("NewVectorAnchorYBox").Value is not null);
        _layout.Set("CanResetVectorAnchor", canChangeNode && _vectorAnchorDraftFields.Count > 0);
        _layout.Set("CanDeleteVectorAnchor", canChangeNode && count > 2);
        _layout.Set("AddedVectorAnchorTarget", _lastAddedVectorAnchor);
        _layout.Set("CanSelectAddedVectorAnchor", canChangeNode && IsCurrentVectorAnchorTarget(_lastAddedVectorAnchor));
        _layout.Set("VectorAnchorNotice", _vectorAnchorDraftFields.Count > 0 && !IsCurrentVectorAnchorDraft()
            ? "These coordinates belong to a previous anchor or edit. Reset them for the current anchor."
            : "Adds a straight incoming point after the selected anchor. The remaining path stays editable; removing an anchor keeps at least two points.");
    }
    private void ClearVectorAnchorDraft() { _vectorAnchorDraftFields.Clear(); _vectorAnchorOwner = null; _vectorAnchorOriginal = null; }
    private void ResetVectorAnchorDraft() { _ = SelectedVectorNode(); ClearVectorAnchorDraft(); RefreshCompositionRows(); }
    private void AddSelectedVectorAnchor()
    {
        if (!IsCurrentVectorAnchorDraft()) { ShowError("Reset the new point coordinates for the current anchor before adding it."); return; }
        var target = _vectorAnchorOriginal!; var x = Find<NumericUpDown>("NewVectorAnchorXBox").Value; var y = Find<NumericUpDown>("NewVectorAnchorYBox").Value;
        if (x is null || y is null) { ShowError("Enter the new anchor’s X and Y coordinates."); return; }
        ChangeComposition("Add path anchor", owner =>
        {
            var result = owner.AddVectorAnchor(target, (double)x.Value, (double)y.Value, out var added);
            _lastAddedVectorAnchor = added; ClearVectorAnchorDraft();
            // Keep the existing selection and its pending inspector fields.
            // The explicit source-bound button can select the newly added node.
            return result;
        });
    }
    private void DeleteSelectedVectorAnchor() => ChangeComposition("Remove path anchor", owner => owner.DeleteVectorAnchor(SelectedVectorNode()));
    private void SelectAddedVectorAnchor(object? parameter)
    {
        if (parameter is not PictureVectorNodeTarget target || !ReferenceEquals(target, _lastAddedVectorAnchor) || !IsCurrentVectorAnchorTarget(target))
            throw new InvalidOperationException("Select the SAME current newly added anchor again.");
        SelectVectorNode(target);
    }
}
