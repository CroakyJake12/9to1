using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Motion;

/// <summary>The platform supplies its original Home readiness and authorised services. No private runtime is started.</summary>
public static class MotionNativeSurface
{
    public static CuiNativeScene CreateScene(MotionCuiWorkspace workspace, ICuiSceneReadiness readiness)
    {
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("motion.timeline", _ => new MotionTimelineControl(workspace));
        return new("motion", "Motion", "Imagine", MotionCuiWorkspace.LoadDocument(), workspace, workspace, readiness) { ControlRegistry = registry };
    }
    public static int RunSetupRequired(string[] args)
    {
        // A standalone process has no original authenticated Home attachment. The host's readiness panel explains recovery.
        var bindings = new CuiViewModel();
        var scene = new CuiNativeScene("motion", "Motion", "Imagine", MotionCuiWorkspace.LoadDocument(), bindings, bindings, new RequiredHome());
        return CuiNativeHost.Run(scene, args);
    }
    private sealed class RequiredHome : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable, "HomeStartupAttachmentUnavailable",
                "Open Motion from Home to connect the shared Files and media services. If Home is not installed, install Home before continuing. Existing Motion projects are preserved."));
        }
    }
}

/// <summary>Specialised CUI Object: graphical projection of canonical frame ranges. Gestures select; typed actions edit.</summary>
internal sealed class MotionTimelineControl : Control
{
    private readonly MotionCuiWorkspace _workspace;
    private const double LabelWidth = 100, RowHeight = 36, RulerHeight = 24;
    public MotionTimelineControl(MotionCuiWorkspace workspace)
    {
        _workspace = workspace; Focusable = true;
        AttachedToVisualTree += (_, _) => _workspace.PropertyChanged += Changed;
        DetachedFromVisualTree += (_, _) => _workspace.PropertyChanged -= Changed;
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => InvalidateVisual();
    private long Duration => Math.Max(1, _workspace.Sequence.VideoTracks.SelectMany(t => t.Elements).Select(e => e.TimelineStart + e.Duration)
        .Concat((_workspace.Sequence.Markers ?? []).Select(marker => marker.Frame + 1)).DefaultIfEmpty(300).Max());
    private IBrush Brush(string key) => this.TryFindResource(key, out var resource) && resource is IBrush brush ? brush : ForegroundFallback;
    private IBrush ForegroundFallback => this.TryFindResource("CuiTextBrush", out var resource) && resource is IBrush brush ? brush : Brushes.Transparent;
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var foreground = Brush("CuiTextBrush"); var accent = Brush("CuiAccentBrush"); var surface = Brush("CuiPanelBrush");
        var width = Math.Max(1, Bounds.Width - LabelWidth); var duration = Duration;
        var typeface = new Typeface(GetValue(TextBlock.FontFamilyProperty), FontStyle.Normal, FontWeight.Medium);
        void Text(string text, double x, double y) => context.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 12, foreground), new(x, y));
        for (var index = 0; index < _workspace.Sequence.VideoTracks.Count; index++)
        {
            var track = _workspace.Sequence.VideoTracks[index]; var top = RulerHeight + index * RowHeight;
            Text(track.Name + (track.Locked ? " [locked]" : ""), 0, top + 8);
            context.DrawRectangle(surface, new Pen(foreground, .5), new Rect(LabelWidth, top + 2, width, RowHeight - 4));
            foreach (var element in track.Elements)
            {
                var x = LabelWidth + (double)element.TimelineStart / duration * width;
                var clipWidth = Math.Max(2, (double)element.Duration / duration * width);
                context.DrawRectangle(accent, new Pen(foreground, element.ElementId == _workspace.SelectedElementId ? 3 : 1), new Rect(x, top + 5, clipWidth, RowHeight - 10));
                if (clipWidth > 60) Text($"{element.TimelineStart}–{element.TimelineStart + element.Duration}", x + 3, top + 10);
            }
        }
        foreach (var marker in _workspace.Sequence.Markers ?? [])
        {
            var x = LabelWidth + (double)marker.Frame / duration * width;
            context.DrawLine(new Pen(accent, 2), new(x, 0), new(x, Bounds.Height));
            context.DrawRectangle(accent, null, new Rect(x - 3, 0, 6, RulerHeight));
            if (x + 60 < Bounds.Width) Text(marker.Name, x + 5, 3);
        }
        var playheadX = LabelWidth + (double)_workspace.Playhead / duration * width;
        context.DrawLine(new Pen(foreground, 2), new(playheadX, 0), new(playheadX, Bounds.Height));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); var position = e.GetPosition(this);
        if (position.X < LabelWidth) return;
        var frame = MotionMarkers.FrameAtPosition(position.X - LabelWidth, Math.Max(1, Bounds.Width - LabelWidth), Duration);
        var tolerance = MotionMarkers.SnapTolerance(8, Math.Max(1, Bounds.Width - LabelWidth), Duration);
        frame = _workspace.SnapTimelineFrame(frame, tolerance);
        if (position.Y < RulerHeight)
        { _workspace.Select(_workspace.SelectedElementId, frame); Focus(); e.Handled = true; return; }
        var track = _workspace.Sequence.VideoTracks.ElementAtOrDefault((int)((position.Y - RulerHeight) / RowHeight));
        var selected = track?.Elements.LastOrDefault(item => item.TimelineStart <= frame && frame < item.TimelineStart + item.Duration);
        _workspace.Select(selected?.ElementId, frame); Focus(); e.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Left or Key.Right)
        {
            _workspace.Select(_workspace.SelectedElementId, Math.Max(0, _workspace.Playhead + (e.Key == Key.Left ? -1 : 1))); e.Handled = true;
        }
    }
}
