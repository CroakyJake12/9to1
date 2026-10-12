using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Avalonia.Media;

namespace NineToOne.Web.Productivity.Present;

public static class PresentBrowserSurface
{
    public static BrowserCuiSurface Create(CuiDocument document, PresentBrowserSession session, Func<bool> reduceMotion)
    {
        var lifetime = new PresentationLifetime(session, reduceMotion);
        var registry = new CuiControlRegistry();
        BrowserCanonicalIcons.Register(registry, ShapePreview);
        registry.RegisterObjectRenderer("PresentSlideCanvas", _ => lifetime.Control);
        registry.RegisterObjectRenderer("PresentThumbnails", _ => lifetime.Thumbnails);
        registry.RegisterObjectRenderer("PresentPlaybackCanvas", _ => lifetime.Playback);
        return new(CuiEditorComponents.Compose(document), session, session, lifetime, registry);
    }

    private static IImage? ShapePreview(string source, Avalonia.Controls.Control target)
    {
        if (source.StartsWith("color:", StringComparison.Ordinal) && Color.TryParse(source[6..], out var color))
            return new DrawingImage(new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(2,2,20,20), 4, 4),
                Brush = new SolidColorBrush(color), Pen = new Pen(new SolidColorBrush(Color.FromArgb(100,128,128,128)), 1) })
                { Viewbox = new Rect(0,0,24,24) };
        if (!source.StartsWith("shape:", StringComparison.Ordinal)) return null;
        var shape = DocumentShapePresets.Create(source[6..]);
        if (!target.Resources.TryGetResource("CuiTextBrush", null, out var resource) || resource is not IBrush brush)
            throw new InvalidOperationException("Shape previews require their scoped CUI palette.");
        var drawings = new DrawingGroup();
        foreach (var path in shape.Paths)
        {
            var geometry = new StreamGeometry();
            using (var writer = geometry.Open())
            {
                writer.SetFillRule(path.FillRule == DocumentVectorFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd);
                foreach (var subpath in path.Subpaths.Where(item => item.Nodes.Count > 0))
                {
                    var first = subpath.Nodes[0];
                    writer.BeginFigure(new(first.X, first.Y), false);
                    foreach (var node in subpath.Nodes.Skip(1))
                    {
                        var end = new Point(node.X, node.Y);
                        if (node.IncomingSegment == DocumentVectorSegmentKind.Cubic && node.Control1 is { } c1 && node.Control2 is { } c2)
                            writer.CubicBezierTo(new(c1.X,c1.Y), new(c2.X,c2.Y), end, true);
                        else if (node.IncomingSegment == DocumentVectorSegmentKind.Quadratic && node.Control1 is { } q)
                            writer.QuadraticBezierTo(new(q.X,q.Y), end, true);
                        else writer.LineTo(end, true);
                    }
                    writer.EndFigure(subpath.Closed);
                }
            }
            drawings.Children.Add(new GeometryDrawing { Geometry = geometry, Pen = new Pen(brush, 4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round) });
        }
        return new DrawingImage(drawings) { Viewbox = new Rect(-8, -8, 116, 116) };
    }
    private sealed class PresentationLifetime : IDisposable
    {
        private readonly PresentBrowserSession _session;
        private bool _disposed;
        private TopLevel? _topLevel;
        public PresentRetainedSceneControl Control { get; }
        public PresentThumbnailControl Thumbnails { get; }
        public PresentRetainedSceneControl Playback { get; }
        public PresentationLifetime(PresentBrowserSession session, Func<bool> reduceMotion)
        {
            _session = session; Control = new(reduceMotion); Playback = new(reduceMotion); Thumbnails = new(reduceMotion);
            _session.CanvasTextCommands = Control;
            _session.EffectiveCanvasZoom = () => Control.EffectiveZoom;
            Control.ZoomRequested += _session.SetCanvasZoom;
            _session.BeginShapeInsertion += Control.BeginShapeInsertion;
            Control.ShapeInsertionRequested += InsertShape;
            Thumbnails.SetPreviewRenderer(PresentRetainedSceneControl.CreatePreviewRenderer());
            Control.LiveTextPreviewed += Preview;
            Control.SelectionChanged += Selection;
            Thumbnails.SelectionChanged += Selection;
            Control.AttachedToVisualTree += Attached;
            session.PropertyChanged += Changed;
            Refresh();
        }
        private void Preview(object? sender, EventArgs args) => _session.RecordRetainedPreview();
        private void Selection(object? sender, EventArgs args) => _session.RecordRetainedSelection();
        private async void InsertShape(string preset, double x, double y, double width, double height)
        {
            if (!_disposed) await _session.CompleteShapeInsertionAsync(preset, x, y, width, height);
        }
        private void Attached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            var nextRoot = TopLevel.GetTopLevel(Control);
            if (ReferenceEquals(_topLevel, nextRoot)) return;
            if (_topLevel is not null) { _topLevel.PropertyChanged -= ViewportChanged; _topLevel.RemoveHandler(InputElement.KeyDownEvent, KeyDown); }
            _topLevel = nextRoot;
            if (_topLevel is null) return;
            _topLevel.PropertyChanged += ViewportChanged;
            _topLevel.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel);
            var attachedRoot = _topLevel;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed && ReferenceEquals(_topLevel, attachedRoot))
                    _session.SetViewportWidth(attachedRoot.ClientSize.Width);
            });
        }
        // The editor is detached while compact Properties is active. Viewport
        // observation belongs to this surface lifetime so widening can restore it.
        private void ViewportChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        { if (args.Property == TopLevel.ClientSizeProperty && _topLevel is not null) _session.SetViewportWidth(_topLevel.ClientSize.Width); }
        private void Changed(object? sender, PropertyChangedEventArgs args)
        { if (Dispatcher.UIThread.CheckAccess()) Refresh(); else Dispatcher.UIThread.Post(Refresh); }
        private void Refresh()
        {
            if (_disposed) return;
            Control.SetInputAllowed(_session.IsPresentationAdmitted && _session.IsWorkspaceActive && !_session.IsBusy && !_session.IsReadOnly && !_session.HasUnknownCommitOutcome && !_session.IsPlaying && _session.Editor is not null);
            Control.SetEditor(_session.Editor);
            Control.SetZoom(_session.CanvasZoom, _session.IsFitToWindow);
            Playback.SetEditor(_session.IsPlaying ? _session.Editor : null);
            Thumbnails.SetEditor(_session.Editor, _session.IsPresentationAdmitted && _session.IsWorkspaceActive && !_session.IsBusy && !_session.HasUnknownCommitOutcome, !_session.IsReadOnly);
        }
        private async void KeyDown(object? sender, KeyEventArgs args)
        {
            if (!_session.IsWorkspaceActive || args.Handled) return;
            if (args.Key == Key.Escape)
            {
                var overlayCommand = _session.TryGetValue("ShowHistory", out var history) && history is true ? "CloseHistory"
                    : _session.TryGetValue("ShowHelp", out var help) && help is true ? "ShowHelp" : null;
                if (overlayCommand is not null && _session.IsActionAvailable(overlayCommand) == true)
                {
                    args.Handled = true; await _session.DispatchAsync(overlayCommand, null); return;
                }
            }
            var primary = args.KeyModifiers.HasFlag(KeyModifiers.Control) || args.KeyModifiers.HasFlag(KeyModifiers.Meta);
            var shift = args.KeyModifiers.HasFlag(KeyModifiers.Shift);
            var textInput = Control.IsTextEditing || args.Source is Avalonia.Controls.Control source &&
                (source is TextBox || source.GetVisualAncestors().OfType<TextBox>().Any());
            var command = _session.IsPlaying ? args.Key switch
            {
                Key.Escape => "ExitPresentation", Key.Right or Key.Space or Key.PageDown => "Advance",
                Key.Left or Key.PageUp => "PlayPrevious", _ => null
            } : primary ? args.Key switch
            {
                Key.S => "Save", Key.M when !textInput => "AddSlide",
                Key.B when !textInput => "Bold", Key.I when !textInput => "Italic",
                Key.U when !textInput => "Underline",
                Key.Z when !textInput => shift ? "Redo" : "Undo", Key.Y when !textInput => "Redo",
                Key.C when !textInput => "Copy", Key.V when !textInput => "Paste",
                Key.X when !textInput => "Cut", Key.D when !textInput => "DuplicateObject", _ => null
            } : !textInput && Control.IsKeyboardFocusWithin ? args.Key switch
            {
                Key.Delete or Key.Back => "DeleteObject", Key.Left => "MoveObjectLeft", Key.Right => "MoveObjectRight",
                Key.Up => "MoveObjectUp", Key.Down => "MoveObjectDown", _ => null
            } : null;
            if (command is null || _session.IsActionAvailable(command) != true) return;
            args.Handled = true;
            await _session.DispatchAsync(command, null);
        }
        public void Dispose()
        {
            if (_disposed) return;
            Control.ZoomRequested -= _session.SetCanvasZoom;
            _session.BeginShapeInsertion -= Control.BeginShapeInsertion;
            Control.ShapeInsertionRequested -= InsertShape;
            if (ReferenceEquals(_session.CanvasTextCommands, Control)) { _session.CanvasTextCommands = null; _session.EffectiveCanvasZoom = null; }
            if (_topLevel is not null) { _topLevel.PropertyChanged -= ViewportChanged; _topLevel.RemoveHandler(InputElement.KeyDownEvent, KeyDown); }
            _topLevel = null;
            Control.AttachedToVisualTree -= Attached;
            Control.SelectionChanged -= Selection; Thumbnails.SelectionChanged -= Selection;
            _disposed = true; _session.PropertyChanged -= Changed; Control.LiveTextPreviewed -= Preview; Control.Dispose(); Playback.Dispose(); Thumbnails.Dispose();
        }
    }
}
