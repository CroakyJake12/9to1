using Avalonia.Controls;
using Haven.Core;

namespace HavenOS.Images;

public sealed partial class MainWindow
{
    private bool _updatingVectorFill;
    private readonly HashSet<string> _vectorFillDraftFields = new(StringComparer.Ordinal);
    private PictureEditorSession? _vectorFillOwner;
    private Guid _vectorFillDocumentId, _vectorFillTargetId, _vectorFillPathId;
    private void ConfigureVectorFillInputs()
    {
        var opacity = Find<NumericUpDown>("VectorFillOpacityBox"); opacity.Minimum = 0; opacity.Maximum = 1;
        opacity.Increment = .05m; opacity.FormatString = "0.###"; opacity.ShowButtonSpinner = false;
        Find<ComboBox>("VectorFillKindBox").ItemsSource = Enum.GetValues<DocumentVectorFillKind>();
        Find<ComboBox>("VectorFillRuleBox").ItemsSource = Enum.GetValues<DocumentVectorFillRule>();
        Find<TextBox>("VectorFillBox").PropertyChanged += (_, change) =>
        { if (change.Property == TextBox.TextProperty) MarkVectorFillDraft("Color"); };
        opacity.PropertyChanged += (_, change) =>
        { if (change.Property == NumericUpDown.ValueProperty) MarkVectorFillDraft("Opacity"); };
        Find<ComboBox>("VectorFillKindBox").PropertyChanged += (_, change) =>
        { if (change.Property == ComboBox.SelectedItemProperty) MarkVectorFillDraft("Kind"); };
        Find<ComboBox>("VectorFillRuleBox").PropertyChanged += (_, change) =>
        { if (change.Property == ComboBox.SelectedItemProperty) MarkVectorFillDraft("FillRule"); };
    }
    private void MarkVectorFillDraft(string field)
    {
        if (_updatingVectorFill) return;
        if (_vectorFillDraftFields.Count == 0)
        {
            _vectorFillOwner = _session; _vectorFillDocumentId = _session?.Document.DocumentId ?? Guid.Empty;
            _vectorFillTargetId = _selectedVector?.TargetId ?? Guid.Empty; _vectorFillPathId = _selectedVectorPath ?? Guid.Empty;
        }
        _vectorFillDraftFields.Add(field); RefreshEditor();
    }
    private bool IsCurrentVectorFillDraft() => _vectorFillDraftFields.Count > 0 && _session is not null &&
        ReferenceEquals(_vectorFillOwner, _session) && _vectorFillDocumentId == _session.Document.DocumentId &&
        _selectedVector?.TargetId == _vectorFillTargetId && _selectedVectorPath == _vectorFillPathId;
    private void RefreshVectorFillFields(DocumentVectorPath? selected, bool canChangePath)
    {
        if (_vectorFillDraftFields.Count > 0 && (!ReferenceEquals(_vectorFillOwner, _session) ||
            _vectorFillDocumentId != _session?.Document.DocumentId)) ClearVectorFillDraft();
        if (_initialized && (_vectorFillDraftFields.Count == 0 || IsCurrentVectorFillDraft())) DisplayVectorFill(selected);
        _layout.Set("CanApplyVectorFill", canChangePath && IsCurrentVectorFillDraft());
        _layout.Set("CanResetVectorFill", canChangePath && _vectorFillDraftFields.Count > 0);
        _layout.Set("VectorFillNotice", _vectorFillDraftFields.Count > 0 && !IsCurrentVectorFillDraft()
            ? "These fill edits belong to another path. Select that path again, or reset to this path’s fill."
            : "None hides the fill. EvenOdd leaves overlapping contours open; NonZero fills contours with the same direction.");
    }
    private void DisplayVectorFill(DocumentVectorPath? selected)
    {
        var fill = selected?.Fill ?? new DocumentVectorFill { Kind = DocumentVectorFillKind.None };
        _updatingVectorFill = true;
        try
        {
            if (!_vectorFillDraftFields.Contains("Kind")) Find<ComboBox>("VectorFillKindBox").SelectedItem = fill.Kind;
            if (!_vectorFillDraftFields.Contains("Color")) Find<TextBox>("VectorFillBox").Text = fill.Color;
            if (!_vectorFillDraftFields.Contains("Opacity")) Find<NumericUpDown>("VectorFillOpacityBox").Value = (decimal)fill.Opacity;
            if (!_vectorFillDraftFields.Contains("FillRule")) Find<ComboBox>("VectorFillRuleBox").SelectedItem = selected?.FillRule ?? DocumentVectorFillRule.EvenOdd;
        }
        finally { _updatingVectorFill = false; }
    }
    private void ClearVectorFillDraft() { _vectorFillDraftFields.Clear(); _vectorFillOwner = null; }
    private PictureVectorPathTarget SelectedFillPath()
    {
        var target = SelectedVector(); DemandSameSelection(target, PictureCompositionTargetKind.Vector);
        return new(target, _selectedVectorPath ?? throw new InvalidOperationException("Select a current shape path."));
    }
    private void ResetSelectedVectorFill() { _ = SelectedFillPath(); ClearVectorFillDraft(); RefreshCompositionRows(); }
    private void ApplySelectedVectorFill()
    {
        if (!IsCurrentVectorFillDraft()) { ShowError("Select the path that owns these fill edits, or reset them for the selected path."); return; }
        var target = SelectedFillPath(); var opacity = Find<NumericUpDown>("VectorFillOpacityBox").Value;
        if (opacity is null) { ShowError("Enter a fill opacity."); return; }
        if (Find<ComboBox>("VectorFillKindBox").SelectedItem is not DocumentVectorFillKind kind ||
            Find<ComboBox>("VectorFillRuleBox").SelectedItem is not DocumentVectorFillRule rule)
        { ShowError("Choose a fill and contour rule."); return; }
        var requested = new DocumentVectorFill { Kind = kind, Color = Find<TextBox>("VectorFillBox").Text ?? "", Opacity = (double)opacity.Value };
        var fields = new HashSet<string>(_vectorFillDraftFields, StringComparer.Ordinal);
        ChangeComposition("Change path fill", owner =>
        {
            var actual = owner.SetVectorFillFields(target, requested, rule, fields);
            ClearVectorFillDraft(); return actual;
        });
    }
}
