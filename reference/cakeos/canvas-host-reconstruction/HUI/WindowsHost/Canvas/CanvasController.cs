using Haven.UI.Components;

namespace CakeOS.HuiWindowsHost.Canvas;

/// <summary>
/// Canvas-specific controller: owns the wiring between the Rnote-equivalent
/// HUI chrome (header, tool strip, pen config, colours, document panel) and
/// the real native session. Every handler writes through to the engine and
/// reports honest status; parity-config calls against an ABI-2 native library
/// surface a rebuild diagnostic instead of faking state.
/// </summary>
internal sealed class CanvasController
{
    private CanvasNativeSession _session;
    private CanvasShape _currentShape = CanvasShape.Rectangle;
    private readonly CanvasToolStrip _tools;
    private readonly CanvasPenConfigPanel _penConfig;
    private readonly CanvasColorPanel _colors;
    private readonly CanvasDocumentPanel _document;
    private readonly CanvasHeaderBar _header;

    public CanvasController(
        CanvasNativeSession session,
        CanvasHeaderBar header,
        CanvasToolStrip tools,
        CanvasPenConfigPanel penConfig,
        CanvasColorPanel colors,
        CanvasDocumentPanel document)
    {
        _session = session;
        _header = header;
        _tools = tools;
        _penConfig = penConfig;
        _colors = colors;
        _document = document;

        _tools.ToolRequested += SelectTool;
        _tools.UndoRequested += (_, _) => Undo();
        _tools.RedoRequested += (_, _) => Redo();
        _tools.ZoomRequested += (_, step) => ZoomStepRequested?.Invoke(step);
        _tools.ShapeRequested += (_, _) => ApplyShaperBuilderDefault();
        _header.ZoomRequested += (_, step) => ZoomStepRequested?.Invoke(step);
        _header.MenuRequested += (_, menu) => MenuRequested?.Invoke(menu);

        _penConfig.BrushStyleChanged += style => Run("Brush style", () =>
        {
            _session.SetBrushStyle(style);
            SyncConfigLabels();
        });
        _penConfig.BrushBuilderChanged += builder => Run("Brush builder", () => _session.SetBrushBuilder(builder));
        _penConfig.BrushWidthChanged += width => Run("Brush width", () =>
        {
            var previous = CurrentTool;
            _session.SetTool(CanvasTool.Pen);
            try { _session.SetStrokeWidth(width); }
            finally { _session.SetTool(previous); }
        });
        _penConfig.ShaperBuilderChanged += shape => Run("Shape", () =>
        {
            _session.SetShape(shape);
            _currentShape = shape;
            RenderingInvalidated?.Invoke();
        });
        _penConfig.ShaperStyleChanged += style => Run("Shaper style", () => _session.SetShaperStyle(style));
        _penConfig.ShaperWidthChanged += width => Run("Shaper width", () =>
        {
            var previous = CurrentTool;
            _session.SetTool(CanvasTool.Shape);
            try { _session.SetStrokeWidth(width); }
            finally { _session.SetTool(previous); _tools.SetTool(previous); }
        });
        _penConfig.ShaperConstraintsChanged += enabled => Run("Constraints", () => _session.SetShaperConstraintsEnabled(enabled));
        _penConfig.TypewriterFontSizeChanged += size => Run("Font size", () => _session.SetTypewriterFontSize(size));
        _penConfig.TypewriterTextWidthChanged += width => Run("Text width", () => _session.SetTypewriterTextWidth(width));
        _penConfig.EraserWidthChanged += width => Run("Eraser width", () => _session.SetEraserWidth(width));
        _penConfig.EraserStyleChanged += style => Run("Eraser mode", () => _session.SetEraserStyle(style));
        _penConfig.SelectorStyleChanged += style => Run("Selector mode", () => _session.SetSelectorStyle(style));
        _penConfig.SelectorLockAspectChanged += locked => Run("Aspect lock", () => _session.SetSelectorLockAspect(locked));
        _penConfig.ToolsStyleChanged += style => Run("Utility tool", () => _session.SetToolsStyle(style));

        _colors.StrokeColorChanged += color => Run("Stroke colour", () =>
        {
            _session.SetStrokeColor(color);
            SyncConfigLabels();
        });
        _colors.FillColorChanged += color => Run("Fill colour", () =>
        {
            _session.SetFillColor(color);
            SyncConfigLabels();
        });

        _document.LayoutChanged += layout => Run("Layout", () =>
        {
            _session.SetLayout(layout);
            RenderingInvalidated?.Invoke();
        });
        _document.BackgroundPatternChanged += pattern => Run("Background pattern", () =>
        {
            _session.SetBackgroundPattern(pattern);
            RenderingInvalidated?.Invoke();
        });
        _document.BackgroundColorChanged += color => Run("Background colour", () =>
        {
            _session.SetBackgroundColor(color);
            RenderingInvalidated?.Invoke();
        });
        _document.PatternColorChanged += color => Run("Pattern colour", () =>
        {
            _session.SetPatternColor(color);
            RenderingInvalidated?.Invoke();
        });
        _document.FormatSizeChanged += (w, h) => Run("Page format", () =>
        {
            _session.SetFormatSize(w, h);
            RenderingInvalidated?.Invoke();
        });
        _document.FormatDpiChanged += dpi => Run("DPI", () => _session.SetFormatDpi(dpi));
        _document.SnapChanged += snap => Run("Snap", () => _session.SetSnapPositions(snap));
        _document.ExportPrefsChanged += (bg, pattern, optimize) => Run("Export prefs", () =>
            _session.SetExportPrefs(bg, pattern, optimize));
        _document.ExportRequested += format => ExportRequested?.Invoke(format);
        _document.SelectionExportRequested += () => SelectionExportRequested?.Invoke();

        CurrentTool = CanvasTool.Pen;
        SyncConfigLabels();
    }

    public CanvasTool CurrentTool { get; private set; }

    public event Action<string>? StatusChanged;
    public event Action? RenderingInvalidated;
    public event Action? HistoryChanged;
    public event Action<int>? ZoomStepRequested;
    public event Action<string>? MenuRequested;
    public event Action<CanvasDocExportFormat>? ExportRequested;
    public event Action? SelectionExportRequested;

    public void SelectTool(CanvasTool tool)
    {
        try
        {
            _session.SetTool(tool);
            CurrentTool = tool;
            _tools.SetTool(tool);
            _penConfig.SetTool(tool);
            SyncConfigLabels();
            StatusChanged?.Invoke($"{tool} selected (Rnote {RnoteName(tool)})");
            ToolChanged?.Invoke(tool);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Tool change failed: {ex.Message}");
        }
    }

    public bool Undo()
    {
        try
        {
            var changed = _session.Undo();
            HistoryChanged?.Invoke();
            StatusChanged?.Invoke(changed ? "Undo" : "Nothing to undo");
            return changed;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Undo failed: {ex.Message}");
            return false;
        }
    }

    public bool Redo()
    {
        try
        {
            var changed = _session.Redo();
            HistoryChanged?.Invoke();
            StatusChanged?.Invoke(changed ? "Redo" : "Nothing to redo");
            return changed;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Redo failed: {ex.Message}");
            return false;
        }
    }

    public void RefreshHistory(CanvasToolStrip? strip = null)
    {
        try { (strip ?? _tools).SetHistory(_session.CanUndo, _session.CanRedo); }
        catch { /* history affordance is best-effort; engine state stays authoritative */ }
    }

    /// <summary>Repoints the controller at a replacement session (New / Open).</summary>
    public void Retarget(CanvasNativeSession session)
    {
        _session = session;
        try { _session.SetTool(CurrentTool); } catch { }
        _tools.SetTool(CurrentTool);
        _penConfig.SetTool(CurrentTool);
        RefreshHistory();
        SyncConfigLabels();
    }

    public event Action<CanvasTool>? ToolChanged;

    private void ApplyShaperBuilderDefault()
    {
        // The strip's Shape request keeps the v2 rectangle default visible.
        _penConfig.SetTool(CanvasTool.Shape);
    }

    private void SyncConfigLabels()
    {
        try
        {
            if (!CanvasNativeSession.SupportsParityConfig)
                return;
            _colors.ApplyState(_session.GetStrokeColor(), _session.GetFillColor());
            // Width/style getters report the active engine tool family, so read
            // each family under its own tool and restore the selected tool.
            var current = CurrentTool;
            try
            {
                _session.SetTool(CanvasTool.Pen);
                _penConfig.ApplyBrushState(_session.GetBrushStyle(), _session.GetStrokeWidth());
                _penConfig.ApplyBrushBuilder(_session.GetBrushBuilder());
                _session.SetTool(CanvasTool.Shape);
                _penConfig.ApplyShaperState(
                    _currentShape,
                    _session.GetShaperStyle(),
                    _session.GetStrokeWidth(),
                    _session.GetShaperConstraintsEnabled());
                _session.SetTool(CanvasTool.Eraser);
                _penConfig.ApplyEraserState(_session.GetEraserWidth(), _session.GetEraserStyle());
                _session.SetTool(CanvasTool.Selector);
                _penConfig.ApplySelectorState(_session.GetSelectorStyle(), _session.GetSelectorLockAspect());
                _session.SetTool(CanvasTool.Tools);
                _penConfig.ApplyToolsState(_session.GetToolsStyle());
                _session.SetTool(CanvasTool.Typewriter);
                _penConfig.ApplyTypewriterState(
                    _session.GetTypewriterFontSize(), _session.GetTypewriterTextWidth());
            }
            finally
            {
                _session.SetTool(current);
            }
            _document.ApplyDocumentState(_session.GetLayout(), _session.GetBackgroundPattern());
        }
        catch
        {
            // Labels are best-effort; the authoritative state stays in the engine.
        }
    }

    private void Run(string label, Action action)
    {
        try
        {
            action();
            StatusChanged?.Invoke($"{label} applied");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"{label} failed: {ex.Message}");
        }
    }

    private static string RnoteName(CanvasTool tool) => tool switch
    {
        CanvasTool.Pen => "brush",
        CanvasTool.Highlighter => "brush/marker",
        CanvasTool.Shape => "shaper",
        CanvasTool.Typewriter => "typewriter",
        CanvasTool.Eraser => "eraser",
        CanvasTool.Selector => "selector",
        CanvasTool.Tools => "tools",
        _ => tool.ToString(),
    };
}
