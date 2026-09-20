using Xunit;

namespace CakeOS.Canvas.App.Tests;

/// <summary>
/// In-memory ICanvasSession double for controller-logic tests. It records
/// calls and mimics history semantics; the real engine is covered by the Rust
/// boundary tests and the runtime smoke tests, never by this stub.
/// </summary>
internal sealed class StubCanvasSession : ICanvasSession
{
    private bool _disposed;
    private readonly List<string> _history = new();
    private int _historyIndex = -1;

    public List<string> Calls { get; } = new();
    public Dictionary<CanvasTool, (CanvasRgba Color, double Width)> Styles { get; } = new();
    public double EraserWidth { get; private set; } = 12d;
    public CanvasEraserStyle EraserStyle { get; private set; } = CanvasEraserStyle.Trash;
    public CanvasTool CurrentTool { get; private set; } = CanvasTool.Pen;
    public byte[] SavedPayload { get; set; } = [1, 2, 3, 4];
    public bool ThrowOnSave { get; set; }
    public bool ReturnEmptyFrame { get; set; }

    public bool CanUndo => _historyIndex >= 0;
    public bool CanRedo => _historyIndex < _history.Count - 1;

    public void SetTool(CanvasTool tool)
    {
        Calls.Add($"SetTool:{tool}");
        CurrentTool = tool;
    }

    public void SetShape(CanvasShape shape) => Calls.Add($"SetShape:{shape}");

    public void SetPenStyle(CanvasTool tool, CanvasRgba color, double width)
    {
        Calls.Add($"SetPenStyle:{tool}:{color.R},{color.G},{color.B},{color.A}:{width}");
        Styles[tool] = (color, width);
    }

    public void SetEraser(double width, CanvasEraserStyle style)
    {
        Calls.Add($"SetEraser:{width}:{style}");
        EraserWidth = width;
        EraserStyle = style;
    }

    public void SetViewportSize(double width, double height) { }
    public void ZoomTo(double zoom) { }
    public void PanBy(double deltaX, double deltaY) { }

    public void BeginStroke(double x, double y, double pressure, double tiltX = 0, double tiltY = 0) =>
        Calls.Add("BeginStroke");

    public void UpdateStroke(double x, double y, double pressure, double tiltX = 0, double tiltY = 0) { }

    public void EndStroke(double x, double y, double pressure, double tiltX = 0, double tiltY = 0)
    {
        Calls.Add("EndStroke");
        _history.Add("stroke");
        _historyIndex = _history.Count - 1;
    }

    public bool Undo()
    {
        if (!CanUndo)
            return false;
        Calls.Add("Undo");
        _historyIndex--;
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
            return false;
        Calls.Add("Redo");
        _historyIndex++;
        return true;
    }

    public CanvasSvgFrame RenderSvg()
    {
        if (ReturnEmptyFrame)
            throw new InvalidOperationException("Canvas native bridge returned an empty SVG frame.");
        return new(new CanvasDocumentBounds(0, 0, 100, 100), "<svg></svg>");
    }

    public byte[] SaveRnote()
    {
        if (ThrowOnSave)
            throw new InvalidOperationException("stub save failure");
        return SavedPayload;
    }

    public void Dispose() => _disposed = true;

    public bool IsDisposed => _disposed;
}
