namespace CakeOS.Canvas.App;

/// <summary>
/// UI-agnostic Canvas application state. Owns tool selection, per-tool style
/// memory, zoom, dirty tracking, document identity and file persistence
/// against an <see cref="ICanvasSession"/>. The Avalonia shell only binds to
/// this; unit tests drive it with a stub session.
/// </summary>
public sealed partial class CanvasController : IDisposable
{
    public const double MinZoom = 0.25d;
    public const double MaxZoom = 32d;
    public const double DefaultZoom = 4d;
    public const double MinToolWidth = 1d;
    public const double MaxToolWidth = 48d;

    public static readonly IReadOnlyList<CanvasRgba> Palette =
    [
        new(0, 0, 0, 1), // black
        new(1, 1, 1, 1), // white
        new(0.898, 0.282, 0.302, 1), // red
        new(0.969, 0.42, 0.082, 1), // orange
        new(1, 0.839, 0.039, 1), // yellow
        new(0.188, 0.643, 0.424, 1), // green
        new(0.243, 0.388, 0.867, 1), // blue
        new(0.557, 0.306, 0.776, 1), // purple
    ];

    private readonly Func<ICanvasSession> _createSession;
    private readonly Func<byte[], ICanvasSession> _restoreSession;
    private ICanvasSession _session;
    private readonly Dictionary<CanvasTool, CanvasPenStyle> _styles = new();
    private double _eraserWidth = 12d;
    private CanvasEraserStyle _eraserStyle = CanvasEraserStyle.Trash;
    private bool _disposed;

    public CanvasController(Func<ICanvasSession> createSession, Func<byte[], ICanvasSession>? restoreSession = null)
    {
        _createSession = createSession ?? throw new ArgumentNullException(nameof(createSession));
        _restoreSession = restoreSession ?? CanvasNativeSession.FromRnote;
        _session = _createSession();
        _styles[CanvasTool.Pen] = new CanvasPenStyle(new CanvasRgba(0, 0, 0, 1), 2);
        _styles[CanvasTool.Highlighter] = new CanvasPenStyle(new CanvasRgba(1, 0.839, 0.039, 0.5), 12);
        _styles[CanvasTool.Shape] = new CanvasPenStyle(new CanvasRgba(0, 0, 0, 1), 2);
        ApplyAllStyles();
        Tool = CanvasTool.Pen;
        _session.SetTool(Tool);
        Shape = CanvasShape.Rectangle;
        Zoom = DefaultZoom;
        DocumentName = "Untitled";
    }

    public event Action? StateChanged;

    public ICanvasSession Session => _session;
    public CanvasTool Tool { get; private set; }
    public CanvasShape Shape { get; private set; }
    public double Zoom { get; private set; }
    public bool IsDirty { get; private set; }
    public string? DocumentPath { get; private set; }
    public string DocumentName { get; private set; }
    public string StatusText { get; private set; } = "Ready";
    public bool CanUndo => _session.CanUndo;
    public bool CanRedo => _session.CanRedo;
    public double EraserWidth => _eraserWidth;
    public CanvasEraserStyle EraserStyle => _eraserStyle;

    public string WindowTitle => IsDirty ? $"{DocumentName} • — Canvas" : $"{DocumentName} — Canvas";

    public CanvasPenStyle CurrentStyle => _styles.TryGetValue(Tool, out var style)
        ? style
        : _styles[CanvasTool.Pen];

    public double CurrentWidth => Tool == CanvasTool.Eraser ? _eraserWidth : CurrentStyle.Width;

    public CanvasRgba CurrentColor => Tool == CanvasTool.Eraser
        ? new CanvasRgba(0, 0, 0, 1)
        : CurrentStyle.Color;

    public void SelectTool(CanvasTool tool)
    {
        ThrowIfDisposed();
        _session.SetTool(tool);
        Tool = tool;
        ApplyStyleFor(tool);
        SetStatus($"{tool} selected");
        Console.WriteLine($"CANVAS_RNOTE_TOOL_SELECTED tool={tool}");
        RaiseChanged();
    }

    public void SelectShape(CanvasShape shape)
    {
        ThrowIfDisposed();
        _session.SetShape(shape);
        Shape = shape;
        // Picking a shape means drawing shapes: also select the shape tool.
        _session.SetTool(CanvasTool.Shape);
        Tool = CanvasTool.Shape;
        ApplyStyleFor(CanvasTool.Shape);
        SetStatus($"Shape: {shape}");
        Console.WriteLine($"CANVAS_RNOTE_TOOL_SELECTED tool={Tool}");
        RaiseChanged();
    }

    public void SetColor(CanvasRgba color)
    {
        ThrowIfDisposed();
        if (Tool is not (CanvasTool.Pen or CanvasTool.Highlighter or CanvasTool.Shape))
            return;
        var width = _styles[Tool].Width;
        var alpha = Tool == CanvasTool.Highlighter ? 0.5 : 1.0;
        var style = new CanvasPenStyle(new CanvasRgba(color.R, color.G, color.B, alpha), width);
        _session.SetPenStyle(Tool, style.Color, style.Width);
        _styles[Tool] = style;
        RaiseChanged();
    }

    public void SetWidth(double width)
    {
        ThrowIfDisposed();
        width = Math.Clamp(width, MinToolWidth, MaxToolWidth);
        if (Tool == CanvasTool.Eraser)
        {
            _session.SetEraser(width, _eraserStyle);
            _eraserWidth = width;
            RaiseChanged();
            return;
        }
        if (Tool is not (CanvasTool.Pen or CanvasTool.Highlighter or CanvasTool.Shape))
            return;
        var style = _styles[Tool] with { Width = width };
        _session.SetPenStyle(Tool, style.Color, style.Width);
        _styles[Tool] = style;
        RaiseChanged();
    }

    public void SetEraserStyle(CanvasEraserStyle style)
    {
        ThrowIfDisposed();
        _session.SetEraser(_eraserWidth, style);
        _eraserStyle = style;
        RaiseChanged();
    }

    public void Undo()
    {
        ThrowIfDisposed();
        var changed = _session.Undo();
        if (changed)
            MarkDirty();
        SetStatus(changed ? "Undone" : "Nothing to undo");
        Console.WriteLine($"CANVAS_RNOTE_HISTORY action=undo changed={(changed ? 1 : 0)}");
        RaiseChanged();
    }

    public void Redo()
    {
        ThrowIfDisposed();
        var changed = _session.Redo();
        if (changed)
            MarkDirty();
        SetStatus(changed ? "Redone" : "Nothing to redo");
        Console.WriteLine($"CANVAS_RNOTE_HISTORY action=redo changed={(changed ? 1 : 0)}");
        RaiseChanged();
    }

    public void SetZoom(double zoom)
    {
        ThrowIfDisposed();
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        SetStatus($"Zoom {Zoom:0.#}x");
        RaiseChanged();
    }

    public void MarkDirty()
    {
        IsDirty = true;
        RaiseChanged();
    }

    public void SetStatus(string status)
    {
        StatusText = status ?? string.Empty;
        RaiseChanged();
    }

    public void NewDocument()
    {
        ThrowIfDisposed();
        var old = _session;
        _session = _createSession();
        old.Dispose();
        ApplyAllStyles();
        _session.SetTool(Tool);
        _session.SetShape(Shape);
        DocumentPath = null;
        DocumentName = "Untitled";
        IsDirty = false;
        SetStatus("New document");
        RaiseChanged();
    }

    public void OpenDocument(string path, byte[] bytes)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);
        var restored = _restoreSession(bytes);
        var frame = restored.RenderSvg();
        if (frame.Bounds.Width <= 0 || frame.Bounds.Height <= 0)
        {
            restored.Dispose();
            throw new InvalidDataException("Document produced invalid render bounds.");
        }
        var old = _session;
        _session = restored;
        old.Dispose();
        ApplyAllStyles();
        _session.SetTool(Tool);
        DocumentPath = Path.GetFullPath(path);
        DocumentName = Path.GetFileNameWithoutExtension(path);
        IsDirty = false;
        SetStatus($"Opened {DocumentName}");
        Console.WriteLine($"CANVAS_RNOTE_DOCUMENT_REOPENED bytes={bytes.Length} path={path}");
        RaiseChanged();
    }

    /// <summary>
    /// Atomic save to the current path. Returns false (with status set) when
    /// there is no path yet or nothing changed; throws only on real IO/engine
    /// failure after setting a truthful status.
    /// </summary>
    public bool Save()
    {
        ThrowIfDisposed();
        if (DocumentPath is null)
            return false;
        if (!IsDirty && File.Exists(DocumentPath))
        {
            SetStatus($"No changes since last save");
            Console.WriteLine($"CANVAS_RNOTE_DOCUMENT_SAVE_SKIPPED path={DocumentPath}");
            return true;
        }
        SaveTo(DocumentPath);
        return true;
    }

    public void SaveTo(string path)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            var payload = _session.SaveRnote();
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var temporary = full + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(payload, 0, payload.Length);
                stream.Flush(true);
            }
            File.Move(temporary, full, overwrite: true);
            if (new FileInfo(full).Length != payload.Length)
                throw new InvalidDataException("Saved file size does not match the document payload.");
            DocumentPath = full;
            DocumentName = Path.GetFileNameWithoutExtension(full);
            IsDirty = false;
            SetStatus($"Saved {DocumentName}");
            Console.WriteLine($"CANVAS_RNOTE_DOCUMENT_SAVED bytes={payload.Length} path={full}");
        }
        catch (Exception exception)
        {
            SetStatus($"Save failed: {exception.Message}");
            Console.WriteLine($"CANVAS_RNOTE_DOCUMENT_SAVE_FAILED reason={exception.Message}");
            throw;
        }
        finally
        {
            RaiseChanged();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ApplyAllStyles()
    {
        foreach (var tool in new[] { CanvasTool.Pen, CanvasTool.Highlighter, CanvasTool.Shape })
            ApplyStyleFor(tool);
        _session.SetEraser(_eraserWidth, _eraserStyle);
    }

    private void ApplyStyleFor(CanvasTool tool)
    {
        if (!_styles.TryGetValue(tool, out var style))
            return;
        _session.SetPenStyle(tool, style.Color, style.Width);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void RaiseChanged() => StateChanged?.Invoke();
}
