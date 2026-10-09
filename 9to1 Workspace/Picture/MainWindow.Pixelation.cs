using Avalonia.Controls;
using Haven.Application;

namespace HavenOS.Images;

public sealed partial class MainWindow
{
    private bool _pixelationDraft, _updatingPixelation;
    private void ConfigurePixelationInputs()
    {
        var input = Find<NumericUpDown>("PixelationBlockSizeBox");
        input.Minimum = 2; input.Maximum = 512; input.Increment = 1; input.FormatString = "0"; input.ShowButtonSpinner = false;
        input.PropertyChanged += (_, change) =>
        { if (change.Property == NumericUpDown.ValueProperty && !_updatingPixelation) _pixelationDraft = true; };
        ResetPixelationFields();
    }
    private PixelationOperation ReadPixelationOperation()
    {
        var bounds = ReadCropBounds(); var block = Find<NumericUpDown>("PixelationBlockSizeBox").Value;
        if (block is null || block != decimal.Truncate(block.Value) || block is < 2 or > 512)
            throw new InvalidDataException("Choose a whole-pixel block size from 2 to 512.");
        return new(new RasterPixelation(bounds.X, bounds.Y, bounds.Width, bounds.Height, (int)block.Value));
    }
    private void SetPixelationBlock(int block)
    {
        _updatingPixelation = true;
        try { Find<NumericUpDown>("PixelationBlockSizeBox").Value = block; }
        finally { _updatingPixelation = false; }
    }
    private void ResetPixelationFields() { SetPixelationBlock(8); _pixelationDraft = false; }
    private void RefreshPixelationFields()
    {
        if (_initialized && !_pixelationDraft && _selectedOperation?.Operation is not PixelationOperation) SetPixelationBlock(8);
    }
    private void SelectPixelationFields(PixelationOperation operation)
    {
        if (!_cropDraft) SetCropField($"{operation.Settings.X}, {operation.Settings.Y}, {operation.Settings.Width}, {operation.Settings.Height}");
        if (!_pixelationDraft) SetPixelationBlock(operation.Settings.BlockSize);
    }
    private void ApplyPixelation()
    {
        try
        {
            var operation = ReadPixelationOperation();
            if (Edit("Pixelate rectangle", document => document.Pixelate(operation.Settings)))
            {
                // Pixelation consumes the SAME entered rectangle, but no crop
                // ratio/size/other adjustment draft. Saved graph stays editable.
                _pixelationDraft = false; _cropDraft = false; RefreshEditor();
                _statusText.Text = "Rectangle pixelated · original preserved · edit this step to change the block size or area.";
            }
        }
        catch (Exception error) when (error is not PictureComparisonPublicationFailure)
        { ShowError("Pixelation could not be applied: " + error.Message); }
    }
}
