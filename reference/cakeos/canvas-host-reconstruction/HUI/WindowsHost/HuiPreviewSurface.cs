using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using CakeOS.HuiWindowsHost.Canvas;
using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiImage = Haven.UI.Components.Image;
using HuiPage = Haven.UI.Components.Page;
using HuiText = Haven.UI.Components.Text;

namespace CakeOS.HuiWindowsHost;

public sealed class HuiPreviewSurface : Control, IHavenMeasureContext, IDisposable
{
    private const string CanvasFrameSource = "cake-canvas://rnote-frame";
    private const double CanvasMinZoom = 1d;
    private const double CanvasMaxZoom = 32d;
    private const double CanvasInitialZoom = 4d;

    private readonly HuiPage _root;
    private readonly HuiButton _action;
    private readonly HuiText _status;
    private readonly HavenLayoutEngine _layout = new();
    private readonly HavenSceneRenderer _renderer = new();
    private readonly HavenInputRouter _input;
    private readonly bool _canvasMode;
    private CanvasNativeSession? _canvasSession;
    private bool _canvasDirty;
    private string? _canvasDocumentPath;
    private readonly HuiImage? _canvasElement;
    private readonly CanvasToolStrip? _canvasTools;
    private readonly CanvasHeaderBar? _canvasHeader;
    private readonly CanvasPenConfigPanel? _canvasPenConfig;
    private readonly CanvasColorPanel? _canvasColors;
    private readonly CanvasDocumentPanel? _canvasDocument;
    private CanvasController? _canvasController;
    private SvgSource? _canvasSvgSource;
    private SvgImage? _canvasSvgImage;
    private CanvasDocumentBounds? _canvasDocumentBounds;
    private CanvasTool _canvasStrokeTool = CanvasTool.Pen;
    private bool _canvasStrokeActive;
    private bool _canvasPanActive;
    private IPointer? _canvasPanPointer;
    private Point _canvasPanLastSurfacePoint;
    private double _canvasZoom = CanvasInitialZoom;
    private double _canvasViewportCenterX;
    private double _canvasViewportCenterY;
    private bool _canvasViewportInitialized;
    private bool _disposed;

    public HuiPreviewSurface()
    {
        Focusable = true;
        ClipToBounds = true;
        _canvasMode = Environment.GetEnvironmentVariable("CAKEOS_HUI_CANVAS_PREVIEW") == "1";

        if (_canvasMode)
        {
            var scene = BuildCanvasScene();
            _root = scene.Root;
            _action = scene.Action;
            _status = scene.Status;
            _canvasElement = scene.Canvas;
            _canvasTools = scene.Tools;
            _canvasHeader = scene.Header;
            _canvasPenConfig = scene.PenConfig;
            _canvasColors = scene.Colors;
            _canvasDocument = scene.Document;
            _canvasSession = new CanvasNativeSession();
            _canvasController = new CanvasController(
                _canvasSession, _canvasHeader, _canvasTools, _canvasPenConfig, _canvasColors, _canvasDocument);
            WireCanvasController(_canvasController);
            scene.Save.Invoked += (_, _) => SaveCanvasDocument();
            scene.Open.Invoked += (_, _) => OpenCanvasDocument();
            InitializeCanvasFrame(_canvasSession);
        }
        else
        {
            (_root, _action, _status) = BuildScene();
        }

        _input = new HavenInputRouter(_root);
    }

    // Application roots use the same layout, renderer, and input router as the preview scenes.
    public HuiPreviewSurface(HuiPage root)
    {
        Focusable = true;
        ClipToBounds = true;
        _root = root;
        _action = null!;
        _status = null!;
        _canvasMode = false;
        _input = new HavenInputRouter(_root);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 960d;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : 600d;
        _layout.Layout(_root, new HavenSize(width, height), HavenPlatform.Windows, this);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _layout.Layout(_root, new HavenSize(finalSize.Width, finalSize.Height), HavenPlatform.Windows, this);
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var scopes = new Stack<IDisposable>();
        try
        {
            foreach (var command in _renderer.Render(_root))
            {
                switch (command)
                {
                    case HavenPushTransformCommand push:
                    {
                        var transform = Matrix.CreateTranslation(-push.Origin.X, -push.Origin.Y)
                            * Matrix.CreateScale(push.Transform.ScaleX, push.Transform.ScaleY)
                            * Matrix.CreateRotation(push.Transform.RotationDegrees * Math.PI / 180d)
                            * Matrix.CreateTranslation(
                                push.Origin.X + push.Transform.TranslateX,
                                push.Origin.Y + push.Transform.TranslateY);
                        scopes.Push(context.PushTransform(transform));
                        continue;
                    }
                    case HavenPushClipCommand clip:
                        scopes.Push(context.PushClip(Rect(clip.Rect)));
                        continue;
                    case HavenPopTransformCommand or HavenPopClipCommand:
                        if (scopes.Count == 0)
                            throw new InvalidOperationException("HUI renderer emitted an unbalanced transform/clip pop.");
                        scopes.Pop().Dispose();
                        continue;
                    case HavenFillRoundedRectCommand fill:
                        context.DrawRectangle(Brush(fill.Brush, fill.Opacity), null, Rect(fill.Rect), fill.Radius, fill.Radius);
                        break;
                    case HavenStrokeRoundedRectCommand stroke:
                        context.DrawRectangle(null, Pen(stroke.Pen, stroke.Opacity), Rect(stroke.Rect), stroke.Radius, stroke.Radius);
                        break;
                    case HavenTextCommand text:
                    {
                        var formatted = Text(text.Layout, Brush(text.Brush, text.Opacity));
                        var y = text.Layout.CenterVertically
                            ? text.Rect.Y + Math.Max(0d, (text.Rect.Height - formatted.Height) / 2d)
                            : text.Rect.Y;
                        context.DrawText(formatted, new Point(text.Rect.X, y));
                        break;
                    }
                    case HavenLineCommand line:
                        context.DrawLine(Pen(line.Pen, line.Opacity), Point(line.Start), Point(line.End));
                        break;
                    case HavenEllipseCommand ellipse:
                        context.DrawEllipse(Brush(ellipse.Brush, ellipse.Opacity), ellipse.Pen is null ? null : Pen(ellipse.Pen, ellipse.Opacity), Rect(ellipse.Rect));
                        break;
                    case HavenImageCommand image:
                        DrawImage(context, image);
                        break;
                    case HavenShadowCommand shadow:
                        DrawEffect(context, shadow.Rect, shadow.Radius, shadow.Shadow.Brush, shadow.Shadow.OffsetX, shadow.Shadow.OffsetY, shadow.Shadow.Blur, shadow.Shadow.Spread, shadow.Opacity);
                        break;
                    case HavenGlowCommand glow:
                        DrawEffect(context, glow.Rect, glow.Radius, glow.Glow.Brush, 0d, 0d, glow.Glow.Blur, 0d, glow.Opacity);
                        break;
                    default:
                        throw new NotSupportedException($"Graphical preview backend does not yet support {command.GetType().Name}.");
                }
            }

            if (scopes.Count != 0)
                throw new InvalidOperationException("HUI renderer emitted unbalanced transform/clip pushes.");
        }
        finally
        {
            while (scopes.Count > 0) scopes.Pop().Dispose();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);

        if (_canvasPanActive && ReferenceEquals(_canvasPanPointer, e.Pointer))
        {
            UpdateCanvasPan(p);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        _input.PointerMoved(new HavenPoint(p.X, p.Y), PointerKind(e.Pointer.Type));

        if (_canvasStrokeActive)
            UpdateCanvasStroke(e, p);

        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);

        if (TryBeginCanvasPan(e, p))
        {
            Focus();
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        _input.PointerPressed(new HavenPoint(p.X, p.Y), PointerKind(e.Pointer.Type), HavenPointerButton.Primary);
        TryBeginCanvasStroke(e, p);
        Focus();
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var p = e.GetPosition(this);

        if (_canvasPanActive && ReferenceEquals(_canvasPanPointer, e.Pointer))
        {
            EndCanvasPan(p);
            e.Pointer.Capture(null);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        _input.PointerReleased(new HavenPoint(p.X, p.Y));

        if (_canvasStrokeActive)
            EndCanvasStroke(e, p);

        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!_canvasMode || _canvasStrokeActive || _canvasPanActive || _canvasElement is null || Math.Abs(e.Delta.Y) < 0.0001d)
            return;

        var point = e.GetPosition(this);
        if (!Rect(_canvasElement.Bounds).Contains(point))
            return;

        ZoomCanvasViewport(point, e.Delta.Y);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var key = e.Key switch
        {
            Key.Enter => HavenKey.Enter,
            Key.Space => HavenKey.Space,
            Key.Tab => HavenKey.Tab,
            Key.Escape => HavenKey.Escape,
            _ => HavenKey.Unknown,
        };
        if (key == HavenKey.Unknown || !_input.KeyDown(key)) return;
        e.Handled = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        var key = e.Key switch
        {
            Key.Enter => HavenKey.Enter,
            Key.Space => HavenKey.Space,
            Key.Tab => HavenKey.Tab,
            Key.Escape => HavenKey.Escape,
            _ => HavenKey.Unknown,
        };
        if (key == HavenKey.Unknown || !_input.KeyUp(key)) return;
        e.Handled = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void RunInputSelfTest()
    {
        if (_action is null || _status is null)
            throw new InvalidOperationException("Input self-test requires the preview scene.");

        _layout.Layout(_root, new HavenSize(Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height)), HavenPlatform.Windows, this);
        var center = new HavenPoint(_action.Bounds.X + _action.Bounds.Width / 2d, _action.Bounds.Y + _action.Bounds.Height / 2d);
        _action.SetState(HavenElementState.Selected, false);
        _action.Accessibility.Selected = false;
        _input.PointerPressed(center);
        if (!_input.PointerReleased(center) || _action.Accessibility.Selected != true)
            throw new InvalidOperationException("HUI pointer activation self-test failed.");

        _action.SetState(HavenElementState.Selected, false);
        _action.Accessibility.Selected = false;
        _input.Focus(_action);
        if (!_input.KeyDown(HavenKey.Enter) || !_input.KeyUp(HavenKey.Enter) || _action.Accessibility.Selected != true)
            throw new InvalidOperationException("HUI keyboard activation self-test failed.");

        if (_canvasMode)
            RunCanvasToolSelfTest();

        _status.Content = _canvasMode
            ? $"Rnote through HUI; input + tools passed; viewport {_canvasZoom:0.##}x"
            : "HUI pointer + keyboard input passed";
        InvalidateMeasure();
        InvalidateVisual();
    }

    public HavenSize MeasureLeaf(HavenElement element, HavenSize available)
    {
        return element switch
        {
            HuiText text => MeasureText(text, available),
            HuiButton button => new HavenSize(Math.Min(available.Width, Math.Max(160, button.Content.Length * 9 + 40)), Math.Min(available.Height, 46)),
            HuiImage => new HavenSize(Math.Min(available.Width, 820), Math.Min(available.Height, 310)),
            _ => new HavenSize(Math.Min(available.Width, 48), Math.Min(available.Height, 48)),
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _canvasSvgImage = null;
        _canvasSvgSource?.Dispose();
        _canvasSvgSource = null;
        _canvasSession?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string DefaultCanvasDocumentPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "CakeOS", "Canvas", "canvas.rnote");

    private void SaveCanvasDocument()
    {
        if (!_canvasMode || _canvasSession is null)
            return;
        if (_canvasStrokeActive || _canvasPanActive)
        {
            _status.Content = "Finish the active Canvas gesture before saving.";
            return;
        }

        try
        {
            var path = _canvasDocumentPath ??= DefaultCanvasDocumentPath();
            if (!_canvasDirty && File.Exists(path))
            {
                _status.Content = $"No changes since last save ({path})";
                Console.WriteLine($"CANVAS_RNOTE_DOCUMENT_SAVE_SKIPPED path={path}");
                InvalidateVisual();
                return;
            }

            var payload = _canvasSession.SaveRnote();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = path + ".tmp";
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(payload, 0, payload.Length);
                stream.Flush(true);
            }
            File.Move(temporaryPath, path, overwrite: true);
            if (new FileInfo(path).Length != payload.Length)
                throw new InvalidDataException("Saved Canvas file size does not match the Rnote payload.");
            _canvasDirty = false;
            _canvasHeader?.SetDocument(Path.GetFileName(path), dirty: false);
            _status.Content = $"Saved {payload.Length} bytes to {path}";
            Console.WriteLine($"CANVAS_RNOTE_DOCUMENT_SAVED bytes={payload.Length} path={path}");
            InvalidateVisual();
        }
        catch (Exception exception)
        {
            _status.Content = $"Save failed: {exception.Message}";
            Console.WriteLine($"CANVAS_RNOTE_DOCUMENT_SAVE_FAILED reason={exception.Message}");
            InvalidateVisual();
        }
    }

    private void OpenCanvasDocument()
    {
        if (!_canvasMode)
            return;
        if (_canvasStrokeActive || _canvasPanActive)
        {
            _status.Content = "Finish the active Canvas gesture before opening a document.";
            return;
        }

        var path = _canvasDocumentPath ??= DefaultCanvasDocumentPath();
        try
        {
            if (!File.Exists(path))
            {
                _status.Content = $"No saved Canvas document at {path} yet. Draw, then Save.";
                Console.WriteLine($"CANVAS_RNOTE_DOCUMENT_REOPEN_EMPTY path={path}");
                InvalidateVisual();
                return;
            }

            var bytes = File.ReadAllBytes(path);
            var restored = CanvasNativeSession.FromRnote(bytes);
            var frame = restored.RenderSvg();
            if (frame.Bounds.Width <= 0 || frame.Bounds.Height <= 0)
            {
                restored.Dispose();
                throw new InvalidDataException("Saved Canvas document produced invalid render bounds.");
            }

            var previous = _canvasSession;
            _canvasSession = restored;
            previous?.Dispose();
            _canvasController?.Retarget(restored);
            _canvasHeader?.SetDocument(Path.GetFileName(path), dirty: false);
            _canvasViewportInitialized = false;
            RefreshCanvasFrame(_canvasSession);
            RefreshCanvasHistory(_canvasSession);
            _canvasDirty = false;
            _status.Content = $"Reopened {bytes.Length} bytes from {path}";
            Console.WriteLine(
                $"CANVAS_RNOTE_DOCUMENT_REOPENED bytes={bytes.Length} width={frame.Bounds.Width:0.###} height={frame.Bounds.Height:0.###} path={path}");
            InvalidateMeasure();
            InvalidateVisual();
        }
        catch (Exception exception)
        {
            _status.Content = $"Reopen failed: {exception.Message}";
            Console.WriteLine($"CANVAS_RNOTE_DOCUMENT_REOPEN_FAILED path={path} reason={exception.Message}");
            InvalidateVisual();
        }
    }

    private void WireCanvasController(CanvasController controller)
    {
        controller.ToolChanged += tool =>
        {
            _canvasStrokeTool = tool;
            InvalidateVisual();
        };
        controller.StatusChanged += message =>
        {
            _status.Content = message;
            InvalidateVisual();
        };
        controller.RenderingInvalidated += () =>
        {
            if (_canvasSession is null) return;
            RefreshCanvasFrame(_canvasSession);
            RefreshCanvasHistory(_canvasSession);
            _canvasDirty = true;
            InvalidateMeasure();
            InvalidateVisual();
        };
        controller.HistoryChanged += () =>
        {
            if (_canvasSession is null) return;
            RefreshCanvasFrame(_canvasSession);
            RefreshCanvasHistory(_canvasSession);
            _canvasDirty = true;
            InvalidateMeasure();
            InvalidateVisual();
        };
        controller.ZoomStepRequested += ZoomCanvasStep;
        controller.MenuRequested += ShowCanvasMenu;
        controller.ExportRequested += ExportCanvasDocument;
        controller.SelectionExportRequested += ExportCanvasSelection;
    }

    private void ZoomCanvasStep(int direction)
    {
        if (!_canvasMode || _canvasElement is null || _canvasStrokeActive || _canvasPanActive)
            return;
        var target = Rect(_canvasElement.Bounds);
        if (target.Width <= 0 || target.Height <= 0)
            return;
        ZoomCanvasViewport(
            new Point(target.X + target.Width / 2d, target.Y + target.Height / 2d),
            direction);
        InvalidateVisual();
    }

    private void ShowCanvasMenu(string menu)
    {
        if (!_canvasMode || _canvasSession is null || _canvasHeader is null)
            return;
        HavenElement anchor = menu switch
        {
            "File" => _canvasHeader.FileButton,
            "Edit" => _canvasHeader.EditButton,
            "View" => _canvasHeader.ViewButton,
            _ => _canvasHeader.HelpButton,
        };
        var session = _canvasSession;
        var controller = _canvasController;
        List<PopupMenuItem> items = menu switch
        {
            "File" => new List<PopupMenuItem>
            {
                new("New", NewCanvasDocument),
                new("Open…", OpenCanvasDocument),
                new("Save", SaveCanvasDocument),
                new("Export SVG", () => ExportCanvasDocument(CanvasDocExportFormat.Svg)),
                new("Export PDF", () => ExportCanvasDocument(CanvasDocExportFormat.Pdf)),
                new("Export XOPP", () => ExportCanvasDocument(CanvasDocExportFormat.Xopp)),
                new("Export selection SVG", ExportCanvasSelection),
            },
            "Edit" => new List<PopupMenuItem>
            {
                new("Undo", () => { controller?.Undo(); }, Enabled: session.CanUndo),
                new("Redo", () => { controller?.Redo(); }, Enabled: session.CanRedo),
                // Selector clipboard (cut/copy/paste/duplicate) needs a platform
                // clipboard + selection-rendering host: NEEDS-FROM-W4, not faked.
                new("Clipboard (needs W4 host)", () => { }, Enabled: false),
            },
            "View" => new List<PopupMenuItem>
            {
                new("Zoom in", () => ZoomCanvasStep(+1)),
                new("Zoom out", () => ZoomCanvasStep(-1)),
                new("Reset zoom 4x", () =>
                {
                    _canvasZoom = CanvasInitialZoom;
                    _canvasHeader?.SetZoom(_canvasZoom);
                    _status.Content = $"HUI viewport {_canvasZoom:0.##}x";
                    InvalidateVisual();
                }),
            },
            _ => new List<PopupMenuItem>
            {
                // Shortcuts/about dialogs need a shared HUI dialog host:
                // NEEDS-FROM-W4. Phase 1 surfaces the content via status.
                new("Keyboard shortcuts", ShowCanvasShortcuts),
                new("About Rnote donor", ShowCanvasAbout),
            },
        };
        var popup = new PopupMenu(anchor, _root, items, 250d, $"{menu} menu");
        _root.Add(popup);
        InvalidateVisual();
    }

    private void ShowCanvasShortcuts()
    {
        _status.Content = "Shortcuts: B pen, M marker, E eraser, S selector, H shaper, T text, Y tools, Ctrl+Z/Y undo/redo, wheel zoom, drag pan";
        Console.WriteLine("CANVAS_RNOTE_SHORTCUTS_SHOWN dialog=needs-w4-host");
        InvalidateVisual();
    }

    private void ShowCanvasAbout()
    {
        _status.Content = "CakeOS Canvas — Rnote rnote-engine/rnote-compose v0.14.2 rev 8cac558, translated into HUI (no GTK)";
        Console.WriteLine("CANVAS_RNOTE_ABOUT_SHOWN dialog=needs-w4-host");
        InvalidateVisual();
    }

    private void NewCanvasDocument()
    {
        if (!_canvasMode || _canvasSession is null)
            return;
        if (_canvasStrokeActive || _canvasPanActive)
        {
            _status.Content = "Finish the active Canvas gesture before creating a document.";
            return;
        }

        var fresh = new CanvasNativeSession();
        var previous = _canvasSession;
        _canvasSession = fresh;
        previous.Dispose();
        _canvasController?.Retarget(fresh);
        _canvasDocumentPath = null;
        _canvasViewportInitialized = false;
        RefreshCanvasHistory(fresh);
        try
        {
            var frame = RefreshCanvasFrame(fresh);
            _canvasHeader?.SetDocument("Untitled.rnote", dirty: true);
            _status.Content = $"New document {frame.Bounds.Width:0} x {frame.Bounds.Height:0}; unsaved changes";
        }
        catch
        {
            _canvasHeader?.SetDocument("Untitled.rnote", dirty: true);
            _status.Content = "New empty document (no renderable content yet)";
        }
        _canvasDirty = true;
        Console.WriteLine("CANVAS_RNOTE_DOCUMENT_NEW");
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void ExportCanvasDocument(CanvasDocExportFormat format)
    {
        if (!_canvasMode || _canvasSession is null)
            return;
        if (_canvasStrokeActive || _canvasPanActive)
        {
            _status.Content = "Finish the active Canvas gesture before exporting.";
            return;
        }

        try
        {
            var bytes = _canvasSession.ExportDoc(format);
            var ext = format switch
            {
                CanvasDocExportFormat.Pdf => "pdf",
                CanvasDocExportFormat.Xopp => "xopp",
                _ => "svg",
            };
            var path = Path.ChangeExtension(_canvasDocumentPath ?? DefaultCanvasDocumentPath(), ext);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            _status.Content = $"Exported {bytes.Length} bytes ({ext}) to {path}";
            Console.WriteLine($"CANVAS_RNOTE_DOCUMENT_EXPORTED format={ext} bytes={bytes.Length} path={path}");
            InvalidateVisual();
        }
        catch (Exception exception)
        {
            _status.Content = $"Export failed: {exception.Message}";
            Console.WriteLine($"CANVAS_RNOTE_DOCUMENT_EXPORT_FAILED reason={exception.Message}");
            InvalidateVisual();
        }
    }

    private void ExportCanvasSelection()
    {
        if (!_canvasMode || _canvasSession is null)
            return;
        if (_canvasStrokeActive || _canvasPanActive)
        {
            _status.Content = "Finish the active Canvas gesture before exporting.";
            return;
        }

        try
        {
            var bytes = _canvasSession.TryExportSelectionSvg();
            if (bytes is null)
            {
                _status.Content = "Nothing selected — draw a selector stroke around strokes first.";
                Console.WriteLine("CANVAS_RNOTE_SELECTION_EXPORT_EMPTY");
                InvalidateVisual();
                return;
            }
            var path = Path.ChangeExtension(_canvasDocumentPath ?? DefaultCanvasDocumentPath(), "selection.svg");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            _status.Content = $"Exported selection ({bytes.Length} bytes) to {path}";
            Console.WriteLine($"CANVAS_RNOTE_SELECTION_EXPORTED bytes={bytes.Length} path={path}");
            InvalidateVisual();
        }
        catch (Exception exception)
        {
            _status.Content = $"Selection export failed: {exception.Message}";
            Console.WriteLine($"CANVAS_RNOTE_SELECTION_EXPORT_FAILED reason={exception.Message}");
            InvalidateVisual();
        }
    }

    private void RefreshCanvasHistory(CanvasNativeSession session) =>
        _canvasTools?.SetHistory(session.CanUndo, session.CanRedo);

    private void RunCanvasToolSelfTest()
    {
        if (_canvasTools is null || _canvasSession is null)
            throw new InvalidOperationException("Canvas HUI tool self-test requires a tool strip and native session.");

        InvokeHuiButton(_canvasTools.EraserButton);
        if (_canvasStrokeTool != CanvasTool.Eraser)
            throw new InvalidOperationException("HUI Eraser button did not select the native eraser tool.");

        InvokeHuiButton(_canvasTools.PenButton);
        if (_canvasStrokeTool != CanvasTool.Pen)
            throw new InvalidOperationException("HUI Pen button did not restore the native pen tool.");

        if (!_canvasSession.CanUndo)
            throw new InvalidOperationException("Canvas HUI tool self-test expected an undoable seeded stroke.");
        InvokeHuiButton(_canvasTools.UndoButton);
        if (!_canvasSession.CanRedo)
            throw new InvalidOperationException("HUI Undo button did not expose redo history.");
        InvokeHuiButton(_canvasTools.RedoButton);
        if (!_canvasSession.CanUndo)
            throw new InvalidOperationException("HUI Redo button did not restore undo history.");

        // Parity tools need a native ABI-3 library; against ABI-2 the strip
        // honestly reports the failure and this gate skips the assertions.
        if (CanvasNativeSession.SupportsParityConfig)
        {
            InvokeHuiButton(_canvasTools.TypewriterButton);
            if (_canvasStrokeTool != CanvasTool.Typewriter)
                throw new InvalidOperationException("HUI Text button did not select the native typewriter tool.");

            InvokeHuiButton(_canvasTools.ToolsButton);
            if (_canvasStrokeTool != CanvasTool.Tools)
                throw new InvalidOperationException("HUI Tools button did not select the native utility tool.");

            InvokeHuiButton(_canvasTools.PenButton);
            if (_canvasStrokeTool != CanvasTool.Pen)
                throw new InvalidOperationException("HUI Pen button did not restore the native pen tool.");

            Console.WriteLine("CANVAS_RNOTE_HUI_PARITY_READY typewriter=1 tools=1 shaper=1 config=1");
        }

        Console.WriteLine("CANVAS_RNOTE_HUI_TOOLBAR_READY pen=1 eraser=1 undo=1 redo=1");
    }

    private void InvokeHuiButton(HuiButton button)
    {
        var center = new HavenPoint(button.Bounds.X + button.Bounds.Width / 2d, button.Bounds.Y + button.Bounds.Height / 2d);
        _input.PointerPressed(center);
        if (!_input.PointerReleased(center))
            throw new InvalidOperationException($"HUI button self-test did not invoke '{button.Name}'.");
    }

    private void InitializeCanvasFrame(CanvasNativeSession session)
    {
        session.SetTool(CanvasTool.Pen);
        _canvasStrokeTool = CanvasTool.Pen;
        session.BeginStroke(120, 120, 0.18, 12, -5);
        session.UpdateStroke(160, 145, 0.35, 10, -4);
        session.UpdateStroke(210, 165, 0.62, 8, -3);
        session.UpdateStroke(260, 205, 0.88, 6, -2);
        session.EndStroke(315, 235, 0.56, 4, -1);

        if (!session.CanUndo || !session.Undo() || !session.CanRedo || !session.Redo())
            throw new InvalidOperationException("Canvas managed/native undo-redo proof failed.");

        _canvasTools?.SetTool(CanvasTool.Pen);
        RefreshCanvasHistory(session);
        var frame = RefreshCanvasFrame(session);
        var abi = CanvasNativeSession.SupportsParityConfig ? 3u : 2u;
        _canvasHeader?.SetZoom(_canvasZoom);
        _canvasHeader?.SetDocument("Untitled.rnote", dirty: false);
        _status.Content = $"Rnote ABI {abi} / SVG / document {frame.Bounds.Width:0} x {frame.Bounds.Height:0} / viewport {_canvasZoom:0.##}x";
        Console.WriteLine(
            $"CANVAS_RNOTE_HUI_RENDER_READY abi={abi} format=svg coordinate=document width={frame.Bounds.Width:0.###} height={frame.Bounds.Height:0.###}");
    }

    private CanvasSvgFrame RefreshCanvasFrame(CanvasNativeSession session)
    {
        var frame = session.RenderSvg();
        if (frame.Bounds.Width <= 0 || frame.Bounds.Height <= 0)
            throw new InvalidOperationException("Canvas native frame returned invalid document bounds.");

        var source = SvgSource.LoadFromSvg(frame.Svg);
        if (source.Picture is null)
        {
            source.Dispose();
            throw new InvalidOperationException("Canvas SVG decoder produced no renderable picture.");
        }

        var image = new SvgImage { Source = source };
        if (image.Size.Width <= 0 || image.Size.Height <= 0)
        {
            source.Dispose();
            throw new InvalidOperationException("Canvas SVG decoder produced invalid image dimensions.");
        }

        var previous = _canvasSvgSource;
        _canvasSvgSource = source;
        _canvasSvgImage = image;
        _canvasDocumentBounds = frame.Bounds;
        if (!_canvasViewportInitialized)
        {
            _canvasViewportCenterX = Math.Clamp(0d, frame.Bounds.X, frame.Bounds.X + frame.Bounds.Width);
            _canvasViewportCenterY = Math.Clamp(0d, frame.Bounds.Y, frame.Bounds.Y + frame.Bounds.Height);
            _canvasViewportInitialized = true;
        }
        previous?.Dispose();
        return frame;
    }

    private bool TryBeginCanvasStroke(PointerPressedEventArgs e, Point surfacePoint)
    {
        if (!_canvasMode || _canvasSession is null || _canvasElement is null || _canvasStrokeActive || _canvasPanActive)
            return false;
        if (e.Pointer.Type == PointerType.Touch)
            return false;

        var properties = e.GetCurrentPoint(this).Properties;
        if (e.Pointer.Type == PointerType.Mouse && !properties.IsLeftButtonPressed)
            return false;
        if (!TrySurfaceToDocument(surfacePoint, requireInside: true, out var documentPoint))
            return false;

        var pressure = PointerPressure(e.Pointer.Type, properties.Pressure);
        _canvasSession.BeginStroke(documentPoint.X, documentPoint.Y, pressure, properties.XTilt, properties.YTilt);
        _canvasStrokeActive = true;
        return true;
    }

    private void UpdateCanvasStroke(PointerEventArgs e, Point surfacePoint)
    {
        if (_canvasSession is null || !TrySurfaceToDocument(surfacePoint, requireInside: false, out var documentPoint))
            return;

        var properties = e.GetCurrentPoint(this).Properties;
        var pressure = PointerPressure(e.Pointer.Type, properties.Pressure);
        _canvasSession.UpdateStroke(documentPoint.X, documentPoint.Y, pressure, properties.XTilt, properties.YTilt);
    }

    private void EndCanvasStroke(PointerReleasedEventArgs e, Point surfacePoint)
    {
        if (_canvasSession is null || !TrySurfaceToDocument(surfacePoint, requireInside: false, out var documentPoint))
        {
            _canvasStrokeActive = false;
            return;
        }

        var properties = e.GetCurrentPoint(this).Properties;
        var pressure = PointerPressure(e.Pointer.Type, properties.Pressure);
        _canvasSession.EndStroke(documentPoint.X, documentPoint.Y, pressure, properties.XTilt, properties.YTilt);
        _canvasStrokeActive = false;
        var frame = RefreshCanvasFrame(_canvasSession);
        RefreshCanvasHistory(_canvasSession);
        _canvasDirty = true;
        _status.Content = $"Pointer {_canvasStrokeTool} committed through HUI to Rnote; viewport {_canvasZoom:0.##}x; unsaved changes";
        Console.WriteLine(
            $"CANVAS_RNOTE_POINTER_STROKE_COMMITTED pointer={e.Pointer.Type} tool={_canvasStrokeTool} pressure={pressure:0.###} width={frame.Bounds.Width:0.###} height={frame.Bounds.Height:0.###} zoom={_canvasZoom:0.###}");
    }

    private bool TryBeginCanvasPan(PointerPressedEventArgs e, Point surfacePoint)
    {
        if (!_canvasMode || _canvasElement is null || _canvasStrokeActive || _canvasPanActive)
            return false;

        var target = Rect(_canvasElement.Bounds);
        if (!target.Contains(surfacePoint))
            return false;

        var properties = e.GetCurrentPoint(this).Properties;
        var isPanGesture = e.Pointer.Type == PointerType.Touch
            || (e.Pointer.Type == PointerType.Mouse && (properties.IsMiddleButtonPressed || properties.IsRightButtonPressed));
        if (!isPanGesture)
            return false;

        _canvasPanActive = true;
        _canvasPanPointer = e.Pointer;
        _canvasPanLastSurfacePoint = surfacePoint;
        return true;
    }

    private void UpdateCanvasPan(Point surfacePoint)
    {
        if (_canvasElement is null || _canvasDocumentBounds is null)
            return;

        var target = Rect(_canvasElement.Bounds);
        if (target.Width <= 0 || target.Height <= 0)
            return;

        var viewport = CanvasViewport(target);
        var delta = surfacePoint - _canvasPanLastSurfacePoint;
        _canvasViewportCenterX -= delta.X / target.Width * viewport.Width;
        _canvasViewportCenterY -= delta.Y / target.Height * viewport.Height;
        _canvasPanLastSurfacePoint = surfacePoint;
        _ = CanvasViewport(target);
        _status.Content = $"HUI viewport pan / {_canvasZoom:0.##}x";
    }

    private void EndCanvasPan(Point surfacePoint)
    {
        UpdateCanvasPan(surfacePoint);
        _canvasPanActive = false;
        _canvasPanPointer = null;
        if (_canvasElement is null)
            return;

        var viewport = CanvasViewport(Rect(_canvasElement.Bounds));
        Console.WriteLine(
            $"CANVAS_RNOTE_VIEWPORT_PAN_COMMITTED zoom={_canvasZoom:0.###} x={viewport.X:0.###} y={viewport.Y:0.###} width={viewport.Width:0.###} height={viewport.Height:0.###}");
    }

    private void ZoomCanvasViewport(Point surfacePoint, double wheelDelta)
    {
        if (_canvasElement is null || _canvasDocumentBounds is not { } bounds)
            return;

        var target = Rect(_canvasElement.Bounds);
        if (target.Width <= 0 || target.Height <= 0)
            return;

        var oldViewport = CanvasViewport(target);
        var u = Math.Clamp((surfacePoint.X - target.X) / target.Width, 0d, 1d);
        var v = Math.Clamp((surfacePoint.Y - target.Y) / target.Height, 0d, 1d);
        var documentX = oldViewport.X + u * oldViewport.Width;
        var documentY = oldViewport.Y + v * oldViewport.Height;
        var nextZoom = Math.Clamp(_canvasZoom * Math.Pow(1.25d, wheelDelta), CanvasMinZoom, CanvasMaxZoom);
        if (Math.Abs(nextZoom - _canvasZoom) < 0.0001d)
            return;

        _canvasZoom = nextZoom;
        var nextSize = CanvasViewportSize(target, bounds, _canvasZoom);
        _canvasViewportCenterX = documentX - (u - 0.5d) * nextSize.Width;
        _canvasViewportCenterY = documentY - (v - 0.5d) * nextSize.Height;
        var viewport = CanvasViewport(target);
        _canvasHeader?.SetZoom(_canvasZoom);
        _status.Content = $"HUI viewport {_canvasZoom:0.##}x";
        Console.WriteLine(
            $"CANVAS_RNOTE_VIEWPORT_ZOOM zoom={_canvasZoom:0.###} x={viewport.X:0.###} y={viewport.Y:0.###} width={viewport.Width:0.###} height={viewport.Height:0.###}");
    }

    private bool TrySurfaceToDocument(Point surfacePoint, bool requireInside, out (double X, double Y) documentPoint)
    {
        documentPoint = default;
        if (_canvasElement is null || _canvasDocumentBounds is null)
            return false;

        var target = Rect(_canvasElement.Bounds);
        if (target.Width <= 0 || target.Height <= 0)
            return false;
        if (requireInside && !target.Contains(surfacePoint))
            return false;

        var viewport = CanvasViewport(target);
        var x = Math.Clamp((surfacePoint.X - target.X) / target.Width, 0d, 1d);
        var y = Math.Clamp((surfacePoint.Y - target.Y) / target.Height, 0d, 1d);
        documentPoint = (viewport.X + x * viewport.Width, viewport.Y + y * viewport.Height);
        return true;
    }

    private Rect CanvasViewport(Rect target)
    {
        if (_canvasDocumentBounds is not { } bounds)
            return default;

        var size = CanvasViewportSize(target, bounds, _canvasZoom);
        if (!_canvasViewportInitialized)
        {
            _canvasViewportCenterX = bounds.X;
            _canvasViewportCenterY = bounds.Y;
            _canvasViewportInitialized = true;
        }

        var minCenterX = bounds.X + size.Width / 2d;
        var maxCenterX = bounds.X + bounds.Width - size.Width / 2d;
        var minCenterY = bounds.Y + size.Height / 2d;
        var maxCenterY = bounds.Y + bounds.Height - size.Height / 2d;
        _canvasViewportCenterX = Math.Clamp(_canvasViewportCenterX, minCenterX, maxCenterX);
        _canvasViewportCenterY = Math.Clamp(_canvasViewportCenterY, minCenterY, maxCenterY);
        return new Rect(
            _canvasViewportCenterX - size.Width / 2d,
            _canvasViewportCenterY - size.Height / 2d,
            size.Width,
            size.Height);
    }

    private static Size CanvasViewportSize(Rect target, CanvasDocumentBounds bounds, double zoom)
    {
        if (target.Width <= 0 || target.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return default;

        var targetAspect = target.Width / target.Height;
        var documentAspect = bounds.Width / bounds.Height;
        double baseWidth;
        double baseHeight;
        if (documentAspect > targetAspect)
        {
            baseHeight = bounds.Height;
            baseWidth = baseHeight * targetAspect;
        }
        else
        {
            baseWidth = bounds.Width;
            baseHeight = baseWidth / targetAspect;
        }

        var clampedZoom = Math.Clamp(zoom, CanvasMinZoom, CanvasMaxZoom);
        return new Size(Math.Max(1d, baseWidth / clampedZoom), Math.Max(1d, baseHeight / clampedZoom));
    }

    private static double PointerPressure(PointerType pointerType, float pressure) =>
        pointerType == PointerType.Mouse || pressure <= 0 ? 0.5d : Math.Clamp(pressure, 0f, 1f);

    private void DrawImage(DrawingContext context, HavenImageCommand command)
    {
        if (!_canvasMode || command.Image.Source != CanvasFrameSource || _canvasSvgImage is null || _canvasDocumentBounds is not { } bounds)
            throw new NotSupportedException($"Graphical preview cannot resolve HUI image source '{command.Image.Source}'.");

        var target = Rect(command.Rect);
        var viewport = CanvasViewport(target);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return;

        var source = new Rect(
            viewport.X - bounds.X,
            viewport.Y - bounds.Y,
            viewport.Width,
            viewport.Height);
        using var opacity = context.PushOpacity(Math.Clamp(command.Opacity, 0d, 1d));
        using var clip = context.PushClip(target);
        context.DrawImage(_canvasSvgImage, source, target);
    }

    private static (HuiPage Root, HuiButton Action, HuiText Status) BuildScene()
    {
        var root = new HuiPage { Name = "PreviewRoot", Layout = HavenLayout.Vertical };
        root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Padding, HavenThickness.Parse("42px"));
        root.SetValue(HavenProperties.Gap, HavenLength.Px(18));

        var eyebrow = new HuiText { Content = "CAKEOS / HUI WINDOWS BACKEND" };
        eyebrow.SetValue(HavenProperties.FontSize, 13d);
        eyebrow.SetValue(HavenProperties.Foreground, "TextSecondary");

        var title = new HuiText { Content = "A real HUI scene, rendered as a Linux desktop window." };
        title.SetValue(HavenProperties.FontSize, 30d);
        title.SetValue(HavenProperties.Foreground, "TextPrimary");

        var body = new HuiText { Content = "GNOME and Mutter are untouched. This preview is an ordinary unprivileged process translating HUI draw commands through the Windows Avalonia backend." };
        body.SetValue(HavenProperties.FontSize, 16d);
        body.SetValue(HavenProperties.Foreground, "TextSecondary");

        var action = new HuiButton { Name = "Action", Content = "Test HUI input" };
        action.SetValue(HavenProperties.Width, HavenLength.Px(190));
        action.SetValue(HavenProperties.Height, HavenLength.Px(46));
        action.ClickActions.Add(HavenAction.Parse("Name.Action -> Selected=True"));

        var status = new HuiText { Name = "Status", Content = "Ready for pointer or keyboard input" };
        status.SetValue(HavenProperties.FontSize, 15d);
        status.SetValue(HavenProperties.Foreground, "TextSecondary");

        root.Add(eyebrow);
        root.Add(title);
        root.Add(body);
        root.Add(action);
        root.Add(status);
        return (root, action, status);
    }

    private static (HuiPage Root, HuiButton Action, HuiText Status, HuiImage Canvas, CanvasToolStrip Tools, HuiButton Save, HuiButton Open, CanvasHeaderBar Header, CanvasPenConfigPanel PenConfig, CanvasColorPanel Colors, CanvasDocumentPanel Document) BuildCanvasScene()
    {
        var root = new HuiPage { Name = "CanvasPreviewRoot", Layout = HavenLayout.Vertical };
        root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Padding, HavenThickness.Parse("24px"));
        root.SetValue(HavenProperties.Gap, HavenLength.Px(10));

        var eyebrow = new HuiText { Content = "CAKEOS / HUI CANVAS / RNOTE ENGINE" };
        eyebrow.SetValue(HavenProperties.FontSize, 12d);
        eyebrow.SetValue(HavenProperties.Foreground, "TextSecondary");

        var title = new HuiText { Content = "Canvas is rendering through HUI." };
        title.SetValue(HavenProperties.FontSize, 28d);
        title.SetValue(HavenProperties.Foreground, "TextPrimary");

        var body = new HuiText
        {
            Content = "Rnote translated into HUI: header menus, six pens with per-tool config, stroke/fill colour, page/background/format, zoom. Wheel zoom is cursor-anchored; touch or middle/right drag pans; primary mouse and pen contact use the selected Rnote tool."
        };
        body.SetValue(HavenProperties.FontSize, 14d);
        body.SetValue(HavenProperties.Foreground, "TextSecondary");

        var header = new CanvasHeaderBar();
        var tools = new CanvasToolStrip();

        var persistenceRow = new Container { Name = "Canvas.Persistence", Layout = HavenLayout.Horizontal };
        persistenceRow.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        persistenceRow.SetValue(HavenProperties.Height, HavenLength.Px(42));
        persistenceRow.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        persistenceRow.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);

        var save = CreateCanvasCommandButton("Canvas.Document.Save", "Save");
        var open = CreateCanvasCommandButton("Canvas.Document.Open", "Open");
        persistenceRow.Add(save);
        persistenceRow.Add(open);

        var penConfig = new CanvasPenConfigPanel();
        var colors = new CanvasColorPanel();
        var document = new CanvasDocumentPanel();
        var sidePanel = new Container { Name = "Canvas.SidePanel", Layout = HavenLayout.Vertical };
        sidePanel.SetValue(HavenProperties.Width, HavenLength.Px(300));
        sidePanel.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        sidePanel.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        sidePanel.Add(penConfig);
        sidePanel.Add(colors);
        sidePanel.Add(document);

        var canvas = new HuiImage { Name = "CanvasFrame", Source = CanvasFrameSource, Fit = HavenImageFit.Contain };
        canvas.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        canvas.SetValue(HavenProperties.Height, HavenLength.Px(310));

        var contentRow = new Container { Name = "Canvas.Content", Layout = HavenLayout.Horizontal };
        contentRow.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        contentRow.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        contentRow.Add(sidePanel);
        contentRow.Add(canvas);

        var action = new HuiButton { Name = "Action", Content = "Test HUI input" };
        action.SetValue(HavenProperties.Width, HavenLength.Px(190));
        action.SetValue(HavenProperties.Height, HavenLength.Px(42));
        action.ClickActions.Add(HavenAction.Parse("Name.Action -> Selected=True"));

        var status = new HuiText { Name = "Status", Content = "Starting Rnote bridge..." };
        status.SetValue(HavenProperties.FontSize, 13d);
        status.SetValue(HavenProperties.Foreground, "TextSecondary");

        root.Add(eyebrow);
        root.Add(title);
        root.Add(body);
        root.Add(header);
        root.Add(tools);
        root.Add(persistenceRow);
        root.Add(contentRow);
        root.Add(action);
        root.Add(status);
        return (root, action, status, canvas, tools, save, open, header, penConfig, colors, document);
    }

    private static HuiButton CreateCanvasCommandButton(string name, string content)
    {
        var button = new HuiButton { Name = name, Content = content };
        button.SetValue(HavenProperties.Width, HavenLength.Px(120));
        button.SetValue(HavenProperties.Height, HavenLength.Px(36));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 10px"));
        button.SetValue(HavenProperties.FontSize, 13d);
        button.Accessibility.AccessibleName = content;
        return button;
    }

    private static HavenSize MeasureText(HuiText text, HavenSize available)
    {
        var fontSize = text.GetValue(HavenProperties.FontSize);
        if (fontSize <= 0) fontSize = 14d;
        var maxWidth = double.IsFinite(available.Width) ? Math.Max(1d, available.Width) : 10000d;
        var formatted = Text(
            new HavenTextLayout(
                text.Content,
                text.GetValue(HavenProperties.FontFamily),
                fontSize,
                text.GetValue(HavenProperties.FontWeight),
                maxWidth),
            Brushes.Transparent);
        return new HavenSize(
            Math.Min(available.Width, formatted.Width + 2d),
            Math.Min(available.Height, formatted.Height + 2d));
    }

    private static FormattedText Text(HavenTextLayout layout, IBrush brush)
    {
        var maxWidth = double.IsFinite(layout.MaxWidth) ? Math.Max(1d, layout.MaxWidth) : 10000d;
        return new FormattedText(layout.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, layout.Italic ? FontStyle.Italic : FontStyle.Normal, Weight(layout.FontWeight), FontStretch.Normal),
            layout.FontSize <= 0 ? 14d : layout.FontSize, brush)
        { MaxTextWidth = maxWidth };
    }

    private static FontWeight Weight(int weight) => weight switch
    {
        >= 800 => FontWeight.ExtraBold,
        >= 700 => FontWeight.Bold,
        >= 600 => FontWeight.SemiBold,
        >= 500 => FontWeight.Medium,
        _ => FontWeight.Normal,
    };

    private static IBrush Brush(HavenBrush brush, double opacity) =>
        new SolidColorBrush(ApplyOpacity(ColorFor(brush), opacity));

    private static Color ColorFor(HavenBrush brush) => brush switch
    {
        HavenSolidBrush solid => Color.FromArgb(solid.A, solid.R, solid.G, solid.B),
        HavenTokenBrush token when token.Token.Contains("Accent", StringComparison.OrdinalIgnoreCase) => Color.Parse("#8A7CFF"),
        HavenTokenBrush token when token.Token.Contains("Secondary", StringComparison.OrdinalIgnoreCase) => Color.Parse("#A8AFBD"),
        HavenTokenBrush token when token.Token.Contains("Text", StringComparison.OrdinalIgnoreCase) => Color.Parse("#F5F7FB"),
        HavenTokenBrush token when token.Token.Contains("Transparent", StringComparison.OrdinalIgnoreCase) => Colors.Transparent,
        HavenTokenBrush => Color.Parse("#242834"),
        _ => Color.Parse("#242834"),
    };

    private static Color ApplyOpacity(Color color, double opacity)
    {
        var alpha = (byte)Math.Clamp(Math.Round(color.A * opacity), 0d, 255d);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static void DrawEffect(
        DrawingContext context,
        HavenRect bounds,
        double radius,
        HavenBrush brush,
        double offsetX,
        double offsetY,
        double blur,
        double spread,
        double opacity)
    {
        var shadows = new BoxShadows(new BoxShadow
        {
            OffsetX = offsetX,
            OffsetY = offsetY,
            Blur = Math.Max(0d, blur),
            Spread = spread,
            Color = ApplyOpacity(ColorFor(brush), opacity),
        });
        context.DrawRectangle(Brushes.Transparent, null, Rect(bounds), radius, radius, shadows);
    }

    private static IPen Pen(HavenPen pen, double opacity) => new Pen(Brush(pen.Brush, opacity), pen.Thickness);
    private static Rect Rect(HavenRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    private static Point Point(HavenPoint point) => new(point.X, point.Y);
    private static HavenPointerKind PointerKind(PointerType type) => type switch
    {
        PointerType.Touch => HavenPointerKind.Touch,
        PointerType.Pen => HavenPointerKind.Pen,
        _ => HavenPointerKind.Mouse,
    };
}
