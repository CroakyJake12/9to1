using Avalonia.Controls;
using Haven.Core;

namespace HavenOS.Images;

public sealed partial class MainWindow
{
    private bool _vectorRotationDraft, _updatingVectorRotation;
    private PictureEditorSession? _vectorRotationOwner;
    private Guid _vectorRotationDocumentId, _vectorRotationTargetId;

    private void ConfigureVectorTransformInputs()
    {
        var input = Find<NumericUpDown>("VectorRotationBox");
        input.Minimum = -180; input.Maximum = 180; input.Increment = 1;
        input.FormatString = "0.###"; input.ShowButtonSpinner = false;
        input.PropertyChanged += (_, change) =>
        {
            if (change.Property != NumericUpDown.ValueProperty || _updatingVectorRotation) return;
            // An existing draft remains attached to its first shape. Selecting
            // another shape never silently reassigns even subsequent typing.
            if (!_vectorRotationDraft)
            {
                _vectorRotationOwner = _session;
                _vectorRotationDocumentId = _session?.Document.DocumentId ?? Guid.Empty;
                _vectorRotationTargetId = _selectedVector?.TargetId ?? Guid.Empty;
            }
            _vectorRotationDraft = true;
            RefreshEditor();
        };
    }

    private bool IsCurrentVectorRotationDraft() => _vectorRotationDraft && _session is not null &&
        ReferenceEquals(_vectorRotationOwner, _session) && _vectorRotationDocumentId == _session.Document.DocumentId &&
        _selectedVector is not null && _selectedVector.TargetId == _vectorRotationTargetId;

    private void RefreshVectorTransformFields(DocumentVectorShape? shape, bool canChangeVector)
    {
        if (_vectorRotationDraft && (!ReferenceEquals(_vectorRotationOwner, _session) ||
            _vectorRotationDocumentId != _session?.Document.DocumentId))
        {
            // Only confirmed owner/document replacement withdraws old drafts.
            _vectorRotationDraft = false; _vectorRotationOwner = null;
        }
        if (_initialized && !_vectorRotationDraft) DisplayVectorRotation(shape);
        _layout.Set("CanApplyVectorRotation", canChangeVector && IsCurrentVectorRotationDraft() &&
            Find<NumericUpDown>("VectorRotationBox").Value is not null);
        _layout.Set("CanResetVectorRotation", canChangeVector && _vectorRotationDraft);
        _layout.Set("CanMirrorVectorHorizontal", canChangeVector && shape is not null && Math.Abs(shape.Transform.ScaleX) <= 1000);
        _layout.Set("CanMirrorVectorVertical", canChangeVector && shape is not null && Math.Abs(shape.Transform.ScaleY) <= 1000);
        var notice = _vectorRotationDraft && !IsCurrentVectorRotationDraft()
            ? "This angle belongs to another shape. Select that shape again, or reset to the selected shape’s angle."
            : "Rotate about this shape’s existing pivot. Mirroring keeps its position and size; the original paths remain editable.";
        if (shape is not null && (Math.Abs(shape.Transform.ScaleX) > 1000 || Math.Abs(shape.Transform.ScaleY) > 1000))
            notice += " Mirroring a retained scale above 1000 is unavailable without changing its size.";
        _layout.Set("VectorTransformNotice", notice);
    }

    private void DisplayVectorRotation(DocumentVectorShape? shape)
    {
        // Same canonical angle normalization, solely for display. Original
        // transform fields remain exact until their own successful edit.
        var display = new DocumentVectorTransform { RotationDegrees = shape?.Transform.RotationDegrees ?? 0 };
        display.Normalize(); _updatingVectorRotation = true;
        try { Find<NumericUpDown>("VectorRotationBox").Value = (decimal)display.RotationDegrees; }
        finally { _updatingVectorRotation = false; }
    }

    private void ResetSelectedVectorRotation()
    {
        var target = SelectedVector(); DemandSameSelection(target, PictureCompositionTargetKind.Vector);
        _vectorRotationDraft = false; _vectorRotationOwner = null;
        RefreshCompositionRows();
    }

    private void ApplySelectedVectorRotation()
    {
        if (!IsCurrentVectorRotationDraft())
        {
            ShowError("Select the shape that owns this angle, or reset the angle for the selected shape."); return;
        }
        var target = SelectedVector(); DemandSameSelection(target, PictureCompositionTargetKind.Vector);
        var value = Find<NumericUpDown>("VectorRotationBox").Value;
        if (value is null || value is < -180 or > 180) { ShowError("Enter an angle from −180 to 180 degrees."); return; }
        ChangeComposition("Rotate shape", owner =>
        {
            var actual = owner.RotateVector(target, (double)value.Value);
            // Only the successful consuming mutation acknowledges this draft.
            _vectorRotationDraft = false; _vectorRotationOwner = null;
            return actual;
        });
    }

    private void MirrorSelectedVector(bool horizontal)
    {
        var target = SelectedVector(); DemandSameSelection(target, PictureCompositionTargetKind.Vector);
        ChangeComposition(horizontal ? "Mirror shape horizontally" : "Mirror shape vertically",
            owner => owner.MirrorVector(target, horizontal));
    }
}
