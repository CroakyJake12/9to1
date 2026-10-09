namespace HavenOS.Images;

/// <summary>Edits the ordered Picture operation graph, validating every dependent canvas.</summary>
public static class PictureOperationEditor
{
    public static PictureDocument Replay(PictureDocument document, IReadOnlyList<PictureOperation> operations,
        int originalWidth, int originalHeight)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(operations);
        var revision = checked(document.Revision + 1);
        var canvas = PictureDocument.Create(originalWidth, originalHeight);
        if ((document.InitialCanvasWidth is { } width && width != originalWidth) ||
            (document.InitialCanvasHeight is { } height && height != originalHeight))
            throw new ArgumentException("The original dimensions must match the Picture source.");
        if (operations.Count > 1_000_000)
            throw new InvalidDataException("The Picture document exceeds the supported operation count.");

        var snapshot = operations.ToArray();
        foreach (var operation in snapshot)
        {
            // Only the current canvas is needed to validate the next operation. Keeping
            // scratch graphs empty avoids repeatedly copying all preceding operations.
            var current = PictureDocument.Create(canvas.CanvasWidth, canvas.CanvasHeight);
            canvas = operation switch
            {
                CropOperation crop => current.Crop(crop.X, crop.Y, crop.Width, crop.Height),
                RotateOperation rotate when rotate.ClockwiseQuarterTurns is >= 1 and <= 3 =>
                    current.Rotate(rotate.ClockwiseQuarterTurns),
                FlipOperation flip => current.Flip(flip.Horizontal),
                StraightenOperation angle => current.Straighten(angle.ClockwiseDegrees, angle.ExpandCanvas),
                ResizeOperation resize => current.Resize(resize.Width, resize.Height),
                CanvasResizeOperation canvasResize => current.ResizeCanvas(canvasResize.Width, canvasResize.Height, canvasResize.OffsetX, canvasResize.OffsetY),
                ColorAdjustmentOperation color => current.AdjustColor(color.Settings, color.WorkingSpace),
                PixelationOperation pixels => current.Pixelate(pixels.Settings),
                BlurOperation blur => current.Blur(blur.Settings),
                _ => throw new InvalidDataException("The Picture document contains an unsupported or invalid operation."),
            };
        }

        // Retain the exact requested graph, including resizes that become no-ops
        // after another operation is moved. Each stack edit is one document revision.
        return new PictureDocument
        {
            DocumentId = document.DocumentId,
            SchemaVersion = Math.Max(document.SchemaVersion, snapshot.Any(operation => operation is BlurOperation) ? 7 :
                snapshot.Any(operation => operation is PixelationOperation) ? 6 :
                snapshot.Any(operation => operation is StraightenOperation) ? 5 :
                snapshot.Any(operation => operation is CanvasResizeOperation or ColorAdjustmentOperation) ? 4 : 1),
            DisplayName = document.DisplayName,
            FileId = document.FileId,
            SourcePath = document.SourcePath,
            SourceRevision = document.SourceRevision,
            CompositionState = document.CompositionState,
            InitialCanvasWidth = document.InitialCanvasWidth,
            InitialCanvasHeight = document.InitialCanvasHeight,
            CanvasWidth = canvas.CanvasWidth,
            CanvasHeight = canvas.CanvasHeight,
            Operations = Array.AsReadOnly(snapshot),
            Revision = revision,
        };
    }

    public static PictureDocument RemoveAt(PictureDocument document, int index, int originalWidth, int originalHeight)
    {
        var operations = CopyOperations(document, index);
        operations.RemoveAt(index);
        return Replay(document, operations, originalWidth, originalHeight);
    }

    public static PictureDocument ReplaceAt(PictureDocument document, int index, PictureOperation replacement,
        int originalWidth, int originalHeight)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var operations = CopyOperations(document, index);
        operations[index] = replacement;
        return Replay(document, operations, originalWidth, originalHeight);
    }

    /// <summary>Moves an operation to its final zero-based position in the resulting graph.</summary>
    public static PictureDocument Move(PictureDocument document, int index, int destinationIndex,
        int originalWidth, int originalHeight)
    {
        var operations = CopyOperations(document, index);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(destinationIndex, operations.Count);
        var operation = operations[index];
        operations.RemoveAt(index);
        operations.Insert(destinationIndex, operation);
        return Replay(document, operations, originalWidth, originalHeight);
    }

    private static List<PictureOperation> CopyOperations(PictureDocument document, int index)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, document.Operations.Count);
        return document.Operations.ToList();
    }
}
