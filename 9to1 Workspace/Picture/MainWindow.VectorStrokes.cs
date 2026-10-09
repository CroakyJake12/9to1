using Avalonia.Controls;
using Haven.Core;

namespace HavenOS.Images;

public sealed partial class MainWindow
{
    private bool _updatingVectorStroke;
    private readonly HashSet<string> _vectorStrokeDraftFields = new(StringComparer.Ordinal);
    private PictureEditorSession? _vectorStrokeOwner;
    private Guid _vectorStrokeDocumentId, _vectorStrokeTargetId, _vectorStrokePathId;

    private void ConfigureVectorStrokeInputs()
    {
        var width = Find<NumericUpDown>("VectorStrokeWidthBox"); width.Minimum = 0; width.Maximum = 10000;
        width.Increment = 1; width.FormatString = "0.###"; width.ShowButtonSpinner = false;
        var opacity = Find<NumericUpDown>("VectorStrokeOpacityBox"); opacity.Minimum = 0; opacity.Maximum = 1;
        opacity.Increment = .05m; opacity.FormatString = "0.###"; opacity.ShowButtonSpinner = false;
        Find<ComboBox>("VectorStrokeCapBox").ItemsSource = Enum.GetValues<DocumentVectorLineCap>();
        Find<ComboBox>("VectorStrokeJoinBox").ItemsSource = Enum.GetValues<DocumentVectorLineJoin>();
        Find<CheckBox>("VectorStrokeEnabledBox").PropertyChanged += (_, change) =>
        { if (change.Property == CheckBox.IsCheckedProperty) MarkVectorStrokeDraft("Enabled"); };
        Find<TextBox>("VectorStrokeColorBox").PropertyChanged += (_, change) =>
        { if (change.Property == TextBox.TextProperty) MarkVectorStrokeDraft("Color"); };
        width.PropertyChanged += (_, change) => { if (change.Property == NumericUpDown.ValueProperty) MarkVectorStrokeDraft("Width"); };
        opacity.PropertyChanged += (_, change) => { if (change.Property == NumericUpDown.ValueProperty) MarkVectorStrokeDraft("Opacity"); };
        Find<ComboBox>("VectorStrokeCapBox").PropertyChanged += (_, change) =>
        { if (change.Property == ComboBox.SelectedItemProperty) MarkVectorStrokeDraft("Cap"); };
        Find<ComboBox>("VectorStrokeJoinBox").PropertyChanged += (_, change) =>
        { if (change.Property == ComboBox.SelectedItemProperty) MarkVectorStrokeDraft("Join"); };
    }

    private void MarkVectorStrokeDraft(string field)
    {
        if (_updatingVectorStroke) return;
        if (_vectorStrokeDraftFields.Count == 0)
        {
            _vectorStrokeOwner = _session; _vectorStrokeDocumentId = _session?.Document.DocumentId ?? Guid.Empty;
            _vectorStrokeTargetId = _selectedVector?.TargetId ?? Guid.Empty; _vectorStrokePathId = _selectedVectorPath ?? Guid.Empty;
        }
        _vectorStrokeDraftFields.Add(field); RefreshEditor();
    }
    private bool IsCurrentVectorStrokeDraft() => _vectorStrokeDraftFields.Count > 0 && _session is not null &&
        ReferenceEquals(_vectorStrokeOwner, _session) && _vectorStrokeDocumentId == _session.Document.DocumentId &&
        _selectedVector?.TargetId == _vectorStrokeTargetId && _selectedVectorPath == _vectorStrokePathId;

    private void RefreshVectorStrokeFields(DocumentVectorPath? selected, bool canChangePath)
    {
        if (_vectorStrokeDraftFields.Count > 0 && (!ReferenceEquals(_vectorStrokeOwner, _session) ||
            _vectorStrokeDocumentId != _session?.Document.DocumentId)) ClearVectorStrokeDraft();
        if (_initialized && (_vectorStrokeDraftFields.Count == 0 || IsCurrentVectorStrokeDraft())) DisplayVectorStroke(selected?.Stroke);
        _layout.Set("CanApplyVectorStroke", canChangePath && IsCurrentVectorStrokeDraft());
        _layout.Set("CanResetVectorStroke", canChangePath && _vectorStrokeDraftFields.Count > 0);
        _layout.Set("VectorStrokeNotice", _vectorStrokeDraftFields.Count > 0 && !IsCurrentVectorStrokeDraft()
            ? "These stroke edits belong to another path. Select that path again, or reset to the selected path’s stroke."
            : "Stroke width uses this shape’s view-box units. Apply preserves the path, fill, placement and original source.");
    }
    private void DisplayVectorStroke(DocumentVectorStroke? selected)
    {
        var stroke = selected ?? new DocumentVectorStroke { Enabled = false };
        _updatingVectorStroke = true;
        try
        {
            if (!_vectorStrokeDraftFields.Contains("Enabled")) Find<CheckBox>("VectorStrokeEnabledBox").IsChecked = stroke.Enabled;
            if (!_vectorStrokeDraftFields.Contains("Color")) Find<TextBox>("VectorStrokeColorBox").Text = stroke.Color;
            if (!_vectorStrokeDraftFields.Contains("Width")) Find<NumericUpDown>("VectorStrokeWidthBox").Value = (decimal)stroke.Width;
            if (!_vectorStrokeDraftFields.Contains("Opacity")) Find<NumericUpDown>("VectorStrokeOpacityBox").Value = (decimal)stroke.Opacity;
            if (!_vectorStrokeDraftFields.Contains("Cap")) Find<ComboBox>("VectorStrokeCapBox").SelectedItem = stroke.Cap;
            if (!_vectorStrokeDraftFields.Contains("Join")) Find<ComboBox>("VectorStrokeJoinBox").SelectedItem = stroke.Join;
        }
        finally { _updatingVectorStroke = false; }
    }
    private void ClearVectorStrokeDraft() { _vectorStrokeDraftFields.Clear(); _vectorStrokeOwner = null; }
    private PictureVectorPathTarget SelectedStrokePath()
    {
        var target = SelectedVector(); DemandSameSelection(target, PictureCompositionTargetKind.Vector);
        return new(target, _selectedVectorPath ?? throw new InvalidOperationException("Select a current shape path."));
    }
    private void ResetSelectedVectorStroke()
    {
        _ = SelectedStrokePath(); ClearVectorStrokeDraft(); RefreshCompositionRows();
    }
    private void ApplySelectedVectorStroke()
    {
        if (!IsCurrentVectorStrokeDraft()) { ShowError("Select the path that owns these stroke edits, or reset them for the selected path."); return; }
        var target = SelectedStrokePath();
        var width = Find<NumericUpDown>("VectorStrokeWidthBox").Value;
        var opacity = Find<NumericUpDown>("VectorStrokeOpacityBox").Value;
        if (width is null || opacity is null) { ShowError("Enter a stroke width and opacity."); return; }
        if (Find<ComboBox>("VectorStrokeCapBox").SelectedItem is not DocumentVectorLineCap cap ||
            Find<ComboBox>("VectorStrokeJoinBox").SelectedItem is not DocumentVectorLineJoin join)
        { ShowError("Choose a stroke cap and join."); return; }
        var requested = new DocumentVectorStroke
        {
            Enabled = Find<CheckBox>("VectorStrokeEnabledBox").IsChecked == true,
            Color = Find<TextBox>("VectorStrokeColorBox").Text ?? "", Width = (double)width.Value, Opacity = (double)opacity.Value,
            Cap = cap, Join = join
        };
        var ownFields = new HashSet<string>(_vectorStrokeDraftFields, StringComparer.Ordinal);
        ChangeComposition("Change path stroke", owner =>
        {
            var actual = owner.SetVectorStrokeFields(target, requested, ownFields);
            ClearVectorStrokeDraft(); return actual;
        });
    }
}
