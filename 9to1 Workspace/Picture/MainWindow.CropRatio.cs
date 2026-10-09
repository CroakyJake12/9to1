using Avalonia.Controls;

namespace HavenOS.Images;

public sealed partial class MainWindow
{
    private bool _cropRatioDraft, _updatingCropRatio;

    private void ConfigureCropRatioInputs()
    {
        var mode = Find<ComboBox>("CropRatioBox");
        mode.ItemsSource = new[] { "Free", "Original", "1:1", "4:3", "3:2", "16:9", "9:16", "Custom" };
        mode.PropertyChanged += (_, change) =>
        {
            if (change.Property == ComboBox.SelectedIndexProperty && !_updatingCropRatio)
            { _cropRatioDraft = true; RefreshCropRatioControls(); }
        };
        foreach (var name in new[] { "CropRatioWidthBox", "CropRatioHeightBox" })
        {
            var input = Find<NumericUpDown>(name);
            input.Minimum = 1; input.Maximum = 32768; input.Increment = 1;
            input.FormatString = "0"; input.ShowButtonSpinner = false;
            input.PropertyChanged += (_, change) =>
            { if (change.Property == NumericUpDown.ValueProperty && !_updatingCropRatio) _cropRatioDraft = true; };
        }
        ResetCropRatioInputs();
    }

    private void ResetCropRatioInputs()
    {
        _updatingCropRatio = true;
        try
        {
            Find<ComboBox>("CropRatioBox").SelectedIndex = 0;
            Find<NumericUpDown>("CropRatioWidthBox").Value = 1;
            Find<NumericUpDown>("CropRatioHeightBox").Value = 1;
            Find<TextBlock>("CropRatioNotice").Text = "Fit a ratio inside these bounds, or apply crop directly. Whole-pixel rounding may slightly change the ratio.";
            _cropRatioDraft = false;
        }
        finally { _updatingCropRatio = false; }
    }

    private void RefreshCropRatioControls()
    {
        var enabled = _session is not null && IsActionAvailable("editor") == true;
        Find<ComboBox>("CropRatioBox").IsEnabled = enabled;
        var custom = enabled && Find<ComboBox>("CropRatioBox").SelectedIndex == 7;
        Find<NumericUpDown>("CropRatioWidthBox").IsEnabled = custom;
        Find<NumericUpDown>("CropRatioHeightBox").IsEnabled = custom;
        Find<Button>("FitCropRatioButton").IsEnabled = enabled;
    }

    private void SelectCropOperationFields(CropOperation operation)
    {
        if (!_cropDraft) SetCropField($"{operation.X}, {operation.Y}, {operation.Width}, {operation.Height}");
        // The saved operation owns its exact geometry, not an inferred preset.
        // A fresh selection never silently reapplies a previously consumed ratio.
        if (!_cropRatioDraft) ResetCropRatioInputs();
    }

    private PictureCropRatioFit ReadRatioCrop(bool selectedEdit)
    {
        var bounds = ReadCropBounds();
        var mode = Find<ComboBox>("CropRatioBox").SelectedIndex;
        if (_session is null) throw new InvalidOperationException("Open an image before fitting a crop ratio.");
        var input = _session.Document;
        using var original = mode == 1 || (selectedEdit && _selectedOperation?.Operation is CropOperation)
            ? PictureCropService.OpenVerifiedSource(input) : null;
        if (selectedEdit && _selectedOperation is { Operation: CropOperation } selected)
        {
            var current = _session.CaptureOperation(selected.Index);
            if (!ReferenceEquals(current.Owner, selected.Owner) || current.DocumentId != selected.DocumentId ||
                current.Revision != selected.Revision || !ReferenceEquals(current.Operation, selected.Operation))
                throw new InvalidOperationException("Revision conflict: select this crop again from the current document.");
            input = PictureOperationEditor.Replay(input, input.Operations.Take(selected.Index).ToArray(),
                original!.PixelSize.Width, original.PixelSize.Height);
        }
        (int width, int height) = mode switch
        {
            0 => (bounds.Width, bounds.Height),
            1 => (original!.PixelSize.Width, original.PixelSize.Height),
            2 => (1, 1), 3 => (4, 3), 4 => (3, 2), 5 => (16, 9), 6 => (9, 16),
            7 => (ReadCustomCropRatio("CropRatioWidthBox"), ReadCustomCropRatio("CropRatioHeightBox")),
            _ => throw new InvalidDataException("Choose Free, Original, a preset, or a valid custom crop ratio.")
        };
        return PictureCropRatio.FitInside(bounds, input.CanvasWidth, input.CanvasHeight, width, height);
    }

    private int ReadCustomCropRatio(string name)
    {
        var value = Find<NumericUpDown>(name).Value;
        if (value is null || value != decimal.Truncate(value.Value) || value is < 1 or > 32768)
            throw new InvalidDataException("Custom crop ratios use positive whole-number width and height from 1 to 32768, such as 3:2.");
        return (int)value.Value;
    }

    private void FitCropRatioDraft()
    {
        try
        {
            var fitted = ReadRatioCrop(selectedEdit: _selectedOperation?.Operation is CropOperation);
            var crop = fitted.Crop;
            SetCropField($"{crop.X}, {crop.Y}, {crop.Width}, {crop.Height}");
            _cropDraft = true;
            PublishCropRatioNotice(fitted, applied: false);
            _statusText.Text = "Crop bounds fitted. Apply crop, or apply values to the selected crop edit.";
        }
        catch (Exception error) when (error is not PictureComparisonPublicationFailure)
        { ShowError("Crop ratio could not be fitted: " + error.Message); }
    }

    private void PublishCropRatioNotice(PictureCropRatioFit fit, bool applied)
    {
        Find<TextBlock>("CropRatioNotice").Text =
            $"{fit.Crop.Width} × {fit.Crop.Height} pixels · " +
            (fit.IsPixelRounded ? "ratio rounded to the nearest whole pixel · " : "") +
            (applied ? "crop applied; original preserved." : "crop draft only; image unchanged.");
    }
}
