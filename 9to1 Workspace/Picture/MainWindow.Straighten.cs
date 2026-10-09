using Avalonia.Controls;

namespace HavenOS.Images;

public sealed partial class MainWindow
{
    private bool _straightenDraft, _updatingStraighten;
    private void ConfigureStraightenInputs()
    {
        var angle = Find<NumericUpDown>("StraightenAngleBox");
        angle.Minimum = -180; angle.Maximum = 180; angle.Increment = .1m;
        angle.FormatString = "0.###"; angle.ShowButtonSpinner = false;
        angle.PropertyChanged += (_, change) =>
        { if (change.Property == NumericUpDown.ValueProperty && !_updatingStraighten) _straightenDraft = true; };
        var mode = Find<ComboBox>("StraightenCanvasModeBox");
        mode.ItemsSource = new[] { "Fit entire image", "Keep canvas · clip rotated edges" };
        mode.PropertyChanged += (_, change) =>
        { if (change.Property == ComboBox.SelectedIndexProperty && !_updatingStraighten) _straightenDraft = true; };
        SetStraightenFields(new(0, true));
    }
    private StraightenOperation ReadStraightenOperation()
    {
        var angle = Find<NumericUpDown>("StraightenAngleBox").Value;
        var mode = Find<ComboBox>("StraightenCanvasModeBox").SelectedIndex;
        if (angle is null || angle is < -180 or > 180 || mode is not (0 or 1))
            throw new InvalidDataException("Choose an angle from −180° to 180° and how the rotated image fits the canvas.");
        return new((double)angle.Value, mode == 0);
    }
    private void SetStraightenFields(StraightenOperation operation)
    {
        _updatingStraighten = true;
        try
        {
            Find<NumericUpDown>("StraightenAngleBox").Value = (decimal)operation.ClockwiseDegrees;
            Find<ComboBox>("StraightenCanvasModeBox").SelectedIndex = operation.ExpandCanvas ? 0 : 1;
        }
        finally { _updatingStraighten = false; }
    }
    private void RefreshStraightenFields()
    {
        if (_initialized && !_straightenDraft && _selectedOperation?.Operation is not StraightenOperation)
            SetStraightenFields(new(0, true));
    }
    private void ApplyStraighten()
    {
        try
        {
            var operation = ReadStraightenOperation();
            if (Edit("Straighten image", document => document.Straighten(operation.ClockwiseDegrees, operation.ExpandCanvas)))
            {
                _straightenDraft = false;
                RefreshStraightenFields();
                _statusText.Text = operation.ExpandCanvas
                    ? "Image rotated · the canvas fits the entire image · original preserved."
                    : "Image rotated · edges are clipped to the canvas · change this edit to recover them.";
            }
        }
        catch (Exception error) when (error is not PictureComparisonPublicationFailure)
        { ShowError("Image could not be straightened: " + error.Message); }
    }
}
