using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Haven.Application;

namespace HavenOS.Images;

public sealed partial class MainWindow
{
    private bool _colorDraft, _updatingColor;
    private sealed record ColorSpaceOption(PictureColorWorkingSpace? Space, string Label)
    { public override string ToString() => Label; }
    private static readonly ColorSpaceOption ChooseColorSpace = new(null, "Choose working colour space");
    private static readonly ColorSpaceOption SrgbColorSpace = new(PictureColorWorkingSpace.Srgb8, "sRGB · 8 bit");

    private void ConfigureColorInputs()
    {
        foreach (var name in new[] { "ColorExposureBox", "ColorBrightnessBox", "ColorContrastBox", "ColorSaturationBox", "ColorGammaBox" })
        {
            var input = Find<NumericUpDown>(name);
            input.Minimum = name switch { "ColorExposureBox" => -10, "ColorGammaBox" => .1m, _ => -1 };
            input.Maximum = name is "ColorExposureBox" or "ColorGammaBox" ? 10 : 1;
            input.Increment = name == "ColorExposureBox" ? .25m : .05m;
            input.FormatString = "0.##"; input.ShowButtonSpinner = false;
            input.PropertyChanged += (_, change) =>
            { if (change.Property == NumericUpDown.ValueProperty && !_updatingColor) _colorDraft = true; };
        }
        var spaces = Find<ComboBox>("ColorWorkingSpaceBox");
        spaces.ItemsSource = new[] { ChooseColorSpace, SrgbColorSpace };
        spaces.PropertyChanged += (_, change) =>
        { if (change.Property == SelectingItemsControl.SelectedItemProperty && !_updatingColor) _colorDraft = true; };
        SetColorFields(new(), null);
    }
    private double ReadColorValue(string name) => (double)(Find<NumericUpDown>(name).Value
        ?? throw new InvalidDataException("Enter a value for each light and colour adjustment."));
    private ColorAdjustmentOperation ReadColorOperation()
    {
        if (!ReferenceEquals(Find<ComboBox>("ColorWorkingSpaceBox").SelectedItem, SrgbColorSpace))
            throw new InvalidDataException("Choose sRGB as the working colour space before applying adjustments.");
        var settings = new RasterColorAdjustment(ReadColorValue("ColorExposureBox"), ReadColorValue("ColorBrightnessBox"),
            ReadColorValue("ColorContrastBox"), ReadColorValue("ColorSaturationBox"), ReadColorValue("ColorGammaBox"));
        settings.Validate(); return new(settings, PictureColorWorkingSpace.Srgb8);
    }
    private void SetColorFields(RasterColorAdjustment settings, PictureColorWorkingSpace? space)
    {
        _updatingColor = true;
        try
        {
            Find<NumericUpDown>("ColorExposureBox").Value = (decimal)settings.ExposureEv;
            Find<NumericUpDown>("ColorBrightnessBox").Value = (decimal)settings.Brightness;
            Find<NumericUpDown>("ColorContrastBox").Value = (decimal)settings.Contrast;
            Find<NumericUpDown>("ColorSaturationBox").Value = (decimal)settings.Saturation;
            Find<NumericUpDown>("ColorGammaBox").Value = (decimal)settings.Gamma;
            Find<ComboBox>("ColorWorkingSpaceBox").SelectedItem = space == PictureColorWorkingSpace.Srgb8 ? SrgbColorSpace : ChooseColorSpace;
        }
        finally { _updatingColor = false; }
    }
    private void ResetColorDraft(bool sourceReplacement = false)
    {
        var selected = Find<ComboBox>("ColorWorkingSpaceBox").SelectedItem;
        PictureColorWorkingSpace? space = ReferenceEquals(selected, SrgbColorSpace) ? PictureColorWorkingSpace.Srgb8 : null;
        if (sourceReplacement && _session is { } owner)
            space = owner.Document.SourcePath is null || owner.Document.Operations.Any(operation => operation is ColorAdjustmentOperation)
                ? PictureColorWorkingSpace.Srgb8 : null;
        SetColorFields(new(), space); _colorDraft = false;
    }
    private void RefreshColorFields()
    {
        if (!_initialized || _colorDraft || _selectedOperation?.Operation is ColorAdjustmentOperation) return;
        ResetColorDraft();
    }
    private void ApplyColorAdjustment()
    {
        try
        {
            var operation = ReadColorOperation();
            if (Edit("Light and colour", document => document.AdjustColor(operation.Settings, operation.WorkingSpace)))
            { SetColorFields(new(), operation.WorkingSpace); _colorDraft = false; }
        }
        catch (Exception error) { ShowError("Light and colour could not be applied: " + error.Message); }
    }
}
