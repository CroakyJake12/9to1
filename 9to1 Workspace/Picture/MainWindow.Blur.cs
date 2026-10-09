using System.Globalization;
using Avalonia.Controls;
using Haven.Application;

namespace HavenOS.Images;

public sealed partial class MainWindow
{
    private bool _blurDraft, _updatingBlur;
    private PictureEditorSession? _blurOwner;
    private Guid _blurDocument;
    private long _blurRevision = -1;
    private PictureOperationTarget? _blurSelection;
    private void ConfigureBlurInputs()
    {
        var radius = Find<NumericUpDown>("BlurRadiusBox");
        radius.Minimum = 1; radius.Maximum = 32; radius.Increment = 1; radius.FormatString = "0"; radius.ShowButtonSpinner = false;
        radius.PropertyChanged += (_, change) => { if (change.Property == NumericUpDown.ValueProperty) ObserveBlurDraft(); };
        Find<TextBox>("BlurBoundsBox").PropertyChanged += (_, change) => { if (change.Property == TextBox.TextProperty) ObserveBlurDraft(); };
        ResetBlurDraft();
    }
    private void CaptureBlurEpoch()
    {
        _blurOwner = _session; _blurDocument = _session?.Document.DocumentId ?? Guid.Empty; _blurRevision = _session?.Document.Revision ?? -1;
        _blurSelection = _selectedOperation?.Operation is BlurOperation ? _selectedOperation : null;
    }
    private void ObserveBlurDraft()
    {
        if (_updatingBlur) return;
        if (!_blurDraft) CaptureBlurEpoch();
        _blurDraft = true; RefreshEditor();
    }
    private bool IsCurrentBlurEpoch()
    {
        if (_session is null || !ReferenceEquals(_blurOwner, _session) || _blurDocument != _session.Document.DocumentId || _blurRevision != _session.Document.Revision) return false;
        if (_blurSelection is null) return _selectedOperation?.Operation is not BlurOperation;
        return _selectedOperation is { } selected && ReferenceEquals(selected.Owner, _blurSelection.Owner)
            && selected.DocumentId == _blurSelection.DocumentId && selected.Revision == _blurSelection.Revision
            && selected.Index == _blurSelection.Index && ReferenceEquals(selected.Operation, _blurSelection.Operation);
    }
    private void SetBlurFields(RasterBlur settings)
    {
        _updatingBlur = true;
        try
        {
            Find<TextBox>("BlurBoundsBox").Text = $"{settings.X}, {settings.Y}, {settings.Width}, {settings.Height}";
            Find<NumericUpDown>("BlurRadiusBox").Value = settings.Radius;
        }
        finally { _updatingBlur = false; }
        CaptureBlurEpoch();
    }
    private void ResetBlurDraft()
    {
        var settings = _selectedOperation?.Operation is BlurOperation selected ? selected.Settings
            : new RasterBlur(0, 0, _session?.Document.CanvasWidth ?? 1, _session?.Document.CanvasHeight ?? 1, 2);
        SetBlurFields(settings); _blurDraft = false;
    }
    private void SelectBlurFields(BlurOperation blur)
    { if (!_blurDraft) SetBlurFields(blur.Settings); }
    private void RefreshBlurFields(bool enabled)
    {
        if (!_blurDraft && !IsCurrentBlurEpoch()) ResetBlurDraft();
        var current = IsCurrentBlurEpoch();
        Find<Button>("ApplyBlurButton").IsEnabled = enabled && current && _selectedOperation?.Operation is not BlurOperation;
        Find<Button>("ResetBlurDraftButton").IsEnabled = enabled;
        if (_selectedOperation?.Operation is BlurOperation && _blurDraft && !current)
            Find<Button>("ReplaceSelectedEditButton").IsEnabled = false;
        _layout.Set("BlurDraftNotice", _blurDraft && !current
            ? "These values belong to a previous image or blur edit. Use the current image or selected blur values to continue."
            : _selectedOperation?.Operation is BlurOperation ? "Values target the selected blur’s original input canvas."
            : "The radius controls how far surrounding pixels soften the rectangle.");
    }
    private BlurOperation ReadBlurOperation()
    {
        if (!IsCurrentBlurEpoch()) throw new InvalidOperationException("Use the current image or selected blur values before applying this draft.");
        var parts = (Find<TextBox>("BlurBoundsBox").Text ?? "").Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var height))
            throw new InvalidDataException("Enter blur bounds as non-negative x, y, width, height in image pixels.");
        var radius = Find<NumericUpDown>("BlurRadiusBox").Value;
        if (radius is null || radius != decimal.Truncate(radius.Value) || radius is < 1 or > 32)
            throw new InvalidDataException("Choose a whole-pixel blur radius from 1 to 32.");
        var settings = new RasterBlur(x, y, width, height, (int)radius.Value); settings.ValidateGeometry(); return new(settings);
    }
    private void ApplyBlur()
    {
        if (_selectedOperation?.Operation is BlurOperation)
            throw new InvalidOperationException("Apply values to the selected blur edit, or select another edit before adding a blur.");
        BlurOperation operation;
        try { operation = ReadBlurOperation(); }
        catch (Exception failure) when (failure is ArgumentException or InvalidDataException or InvalidOperationException)
        { ShowError("Blur could not be applied: " + failure.Message); return; }
        if (Edit("Blur rectangle", document => document.Blur(operation.Settings)))
        {
            _blurDraft = false; RefreshEditor();
            _statusText.Text = "Rectangle blurred · original preserved · edit this step to change the radius or area.";
        }
    }
}
