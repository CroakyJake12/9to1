using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Canvas;

/// <summary>
/// Standalone HavenOS Canvas app surface. The surface owns app/session concerns
/// while every creative mutation is delegated to Haven's existing Canvas engine.
/// </summary>
public sealed class CanvasAppSurface
{
    public const string Route = "haven://apps/canvas";

    private readonly CanvasInteractionController _interaction;

    public CanvasAppSurface(NotesDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        if (!CanvasDocumentModel.IsCanvasDocument(document))
            throw new ArgumentException("CanvasAppSurface requires a Haven Canvas document.", nameof(document));

        _interaction = new CanvasInteractionController(CanvasDocumentModel.GetBoard(document));
        SyncDocumentBoard();
    }

    public static CanvasAppSurface Create(string? title = null) =>
        new(CanvasDocumentModel.Create(title));

    /// <summary>
    /// Creates and durably saves a native Canvas document through the shared Notes repository.
    /// </summary>
    public static async Task<CanvasAppSurface> CreateAsync(
        INotesRepository repository,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var document = CanvasDocumentModel.Create(title);
        await repository.SaveAsync(document, "Created Canvas document", cancellationToken).ConfigureAwait(false);
        return new CanvasAppSurface(document);
    }

    /// <summary>
    /// Opens a native Canvas document by its persisted identity. Non-Canvas Notes documents are rejected.
    /// </summary>
    public static async Task<CanvasAppSurface?> OpenAsync(
        INotesRepository repository,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (documentId == Guid.Empty)
            throw new ArgumentException("Document ID cannot be empty.", nameof(documentId));

        var document = await repository.LoadAsync(documentId, cancellationToken).ConfigureAwait(false);
        if (document is null) return null;
        if (!CanvasDocumentModel.IsCanvasDocument(document))
            throw new InvalidDataException("The requested Notes document is not a Haven Canvas document.");

        return new CanvasAppSurface(document);
    }

    /// <summary>
    /// Saves the current Canvas document through the shared Notes repository.
    /// </summary>
    public Task<NotesSaveResult> SaveAsync(
        INotesRepository repository,
        string reason = "Saved Canvas document",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var saveReason = string.IsNullOrWhiteSpace(reason) ? "Saved Canvas document" : reason.Trim();
        return repository.SaveAsync(Document, saveReason, cancellationToken);
    }

    public NotesDocument Document { get; }

    public NotesCanvasData Board => _interaction.Board;

    public CanvasInteractionController Interaction => _interaction;

    public CanvasSurfaceSnapshot Snapshot => new(
        Board.Objects.Count,
        Board.Strokes.Count,
        Board.Zoom,
        Board.OffsetX,
        Board.OffsetY);

    public NotesCanvasObject AddTextNote(string? text)
    {
        var value = _interaction.AddObject(NotesCanvasObjectKind.Text, text);
        SyncDocumentBoard();
        return value;
    }

    public NotesCanvasObject AddShape(string? label = null)
    {
        var value = _interaction.AddObject(NotesCanvasObjectKind.Shape, label);
        SyncDocumentBoard();
        return value;
    }

    public bool MoveObject(Guid id, double x, double y, double gridSize = 0)
    {
        _interaction.GridSize = Math.Max(0, gridSize);
        _interaction.SelectObject(id);
        var changed = _interaction.MoveSelected(x, y);
        if (changed) SyncDocumentBoard();
        return changed;
    }

    public NotesCanvasObject? Connect(Guid sourceId, Guid targetId, string? label = null)
    {
        var connector = _interaction.Connect(sourceId, targetId, label);
        if (connector is not null) SyncDocumentBoard();
        return connector;
    }

    public bool DrawStroke(IReadOnlyList<CanvasPointerSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count < 2)
            throw new ArgumentException("A stroke needs at least two pointer samples.", nameof(samples));
        if (samples.Any(sample => !double.IsFinite(sample.ViewportX)
            || !double.IsFinite(sample.ViewportY)
            || !double.IsFinite(sample.Pressure)
            || !double.IsFinite(sample.TiltX)
            || !double.IsFinite(sample.TiltY)))
            throw new ArgumentException("Pointer samples must contain finite coordinates, pressure, and tilt.", nameof(samples));

        var previousTool = _interaction.Tool;
        try
        {
            _interaction.ReleaseInteraction();
            _interaction.Tool = CanvasTool.Pen;
            var changed = _interaction.Begin(samples[0]);
            for (var index = 1; index < samples.Count - 1; index++)
                changed |= _interaction.Move(samples[index]);
            changed |= _interaction.End(samples[^1]);
            if (changed) SyncDocumentBoard();
            return changed;
        }
        finally
        {
            _interaction.Tool = previousTool;
        }
    }

    public bool Pan(double startX, double startY, double endX, double endY)
    {
        if (!double.IsFinite(startX) || !double.IsFinite(startY)
            || !double.IsFinite(endX) || !double.IsFinite(endY))
            throw new ArgumentOutOfRangeException(nameof(startX), "Pan coordinates must be finite.");

        var previousTool = _interaction.Tool;
        try
        {
            _interaction.ReleaseInteraction();
            _interaction.Tool = CanvasTool.Pan;
            _interaction.Begin(new CanvasPointerSample(startX, startY));
            var changed = _interaction.Move(new CanvasPointerSample(endX, endY));
            _interaction.End(new CanvasPointerSample(endX, endY));
            if (changed) SyncDocumentBoard();
            return changed;
        }
        finally
        {
            _interaction.Tool = previousTool;
        }
    }

    public void SetZoom(double zoom)
    {
        _interaction.SetZoom(zoom);
        SyncDocumentBoard();
    }

    public bool Undo()
    {
        var changed = _interaction.Undo();
        if (changed) SyncDocumentBoard();
        return changed;
    }

    public bool Redo()
    {
        var changed = _interaction.Redo();
        if (changed) SyncDocumentBoard();
        return changed;
    }

    private void SyncDocumentBoard() =>
        CanvasDocumentModel.ReplaceBoard(Document, _interaction.Board);
}

public readonly record struct CanvasSurfaceSnapshot(
    int ObjectCount,
    int StrokeCount,
    double Zoom,
    double OffsetX,
    double OffsetY);
