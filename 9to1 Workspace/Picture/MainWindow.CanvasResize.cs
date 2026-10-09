using Avalonia.Controls;

namespace HavenOS.Images;

public sealed partial class MainWindow
{
    private bool _canvasDraft, _updatingCanvas;

    private void ConfigureCanvasResizeInputs()
    {
        foreach (var name in new[] { "CanvasWidthBox", "CanvasHeightBox", "CanvasOffsetXBox", "CanvasOffsetYBox" })
        {
            var input = Find<NumericUpDown>(name);
            input.Minimum = name is "CanvasWidthBox" or "CanvasHeightBox" ? 1 : -32768;
            input.Maximum = 32768; input.Increment = 1; input.FormatString = "0";
            input.ShowButtonSpinner = false;
            // Observe the actual Value property before the CUI command pipeline
            // can refresh availability. Queued input events cannot erase drafts.
            input.PropertyChanged += (_, change) =>
            { if (change.Property == NumericUpDown.ValueProperty && !_updatingCanvas) _canvasDraft = true; };
        }
    }

    private int ReadCanvasInteger(string name)
    {
        var value = Find<NumericUpDown>(name).Value;
        if (value is null || value != decimal.Truncate(value.Value) || value < -32768 || value > 32768)
            throw new InvalidDataException("Enter whole pixel values for canvas size and image placement.");
        return (int)value.Value;
    }

    private CanvasResizeOperation ReadCanvasResize() => new(ReadCanvasInteger("CanvasWidthBox"),
        ReadCanvasInteger("CanvasHeightBox"), ReadCanvasInteger("CanvasOffsetXBox"), ReadCanvasInteger("CanvasOffsetYBox"));

    private void SetCanvasResizeFields(CanvasResizeOperation operation)
    {
        _updatingCanvas = true;
        try
        {
            Find<NumericUpDown>("CanvasWidthBox").Value = operation.Width;
            Find<NumericUpDown>("CanvasHeightBox").Value = operation.Height;
            Find<NumericUpDown>("CanvasOffsetXBox").Value = operation.OffsetX;
            Find<NumericUpDown>("CanvasOffsetYBox").Value = operation.OffsetY;
        }
        finally { _updatingCanvas = false; }
    }

    private void RefreshCanvasResizeFields()
    {
        if (!_initialized || _canvasDraft || _session is null || _selectedOperation?.Operation is CanvasResizeOperation) return;
        var document = _session.Document;
        SetCanvasResizeFields(new(document.CanvasWidth, document.CanvasHeight, 0, 0));
    }

    private void ApplyCanvasResize()
    {
        try
        {
            var operation = ReadCanvasResize();
            if (Edit("Resize canvas", document => document.ResizeCanvas(operation.Width, operation.Height, operation.OffsetX, operation.OffsetY)))
            {
                _canvasDraft = false;
                RefreshCanvasResizeFields();
                _statusText.Text = "Canvas resized · image pixels kept at their original size · added space is transparent.";
            }
        }
        catch (Exception error) { ShowError("Canvas could not be resized: " + error.Message); }
    }

    private void CenterCanvasContent()
    {
        if (_session is null) return;
        try
        {
            var width = ReadCanvasInteger("CanvasWidthBox"); var height = ReadCanvasInteger("CanvasHeightBox");
            if (width < 1 || height < 1 || (long)width * height > 100_000_000)
                throw new ArgumentOutOfRangeException(nameof(width), "Choose valid canvas dimensions before centering.");
            var current = _session.Document;
            // Editing a prior canvas operation centers ITS input, not a later
            // cropped/resampled output. Shared replay validates every dependency.
            if (_selectedOperation is { Operation: CanvasResizeOperation } selected)
            {
                using var original = PictureCropService.OpenVerifiedSource(current);
                current = PictureOperationEditor.Replay(current, current.Operations.Take(selected.Index).ToArray(),
                    original.PixelSize.Width, original.PixelSize.Height);
            }
            var offsetX = (int)Math.Floor((width - current.CanvasWidth) / 2d);
            var offsetY = (int)Math.Floor((height - current.CanvasHeight) / 2d);
            SetCanvasResizeFields(new(width, height, offsetX, offsetY));
            _canvasDraft = true;
            _statusText.Text = "Image centered in the canvas draft. Apply canvas size, or apply values to the selected edit.";
        }
        catch (Exception error) { ShowError("Image could not be centered: " + error.Message); }
    }
}
