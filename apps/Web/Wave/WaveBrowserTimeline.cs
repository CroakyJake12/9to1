using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Infrastructure.Media;
using HavenOS.Apps.Wave;

namespace NineToOne.Web.Wave;

/// <summary>CUI-owned projection of canonical track/clip ranges and the shared PCM analyser.
/// Selection and placement call owner session APIs; displayed samples never become saved source.</summary>
internal sealed class WaveBrowserTimeline : Control, IDisposable
{
    private const double RowHeight = 108, RulerHeight = 34;
    private readonly WaveBrowserSession _session;
    private readonly Dictionary<Guid, (string Key, PcmWaveformRange? Data)> _peaks = [];
    private CuiContainerControl? _root;
    private bool _disposed, _updating;
    private (Guid Clip, long OriginalFrame, double PointerX)? _drag;
    private double _dragOffset;
    private double LabelWidth => Bounds.Width < 600 ? 100 : 164;
    private double DisplaySeconds => Math.Max(10, _session.Duration * 1.15);
    private double PixelsPerSecond => Math.Max(1, Bounds.Width - LabelWidth - 16) / DisplaySeconds;
    public WaveBrowserTimeline(WaveBrowserSession session)
    {
        _session = session; Focusable = true; MinHeight = 360;
        session.PropertyChanged += Changed; AttachedToVisualTree += Attached;
        RefreshPeaks();
    }
    private void Attached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (_root is not null) _root.SizeChanged -= Resized;
        _root = this.GetLogicalAncestors().OfType<CuiContainerControl>().FirstOrDefault(x => x.Name == "wave-workspace");
        if (_root is not null) _root.SizeChanged += Resized;
        Dispatcher.UIThread.Post(ApplyLayout);
    }
    private void Resized(object? sender, SizeChangedEventArgs args) => ApplyLayout();
    private void ApplyLayout()
    {
        if (_disposed || _root is null || _updating) return;
        _updating = true;
        try
        {
            var compact = _root.Bounds.Width is > 0 and < 850; _session.SetCompact(compact);
            var nodes = AuthoredControls(_root).ToArray();
            if (nodes.OfType<CuiContainerControl>().FirstOrDefault(x => x.Name == "wave-body") is { } body)
                Set(body,"ColumnSizes",compact || !_session.ShowInspector ? "1fr" : "1fr 280");
            if (nodes.OfType<CuiContainerControl>().FirstOrDefault(x => x.Name == "wave-inspector") is { } inspector)
            {
                Set(inspector,"Active",_session.ShowInspector); Set(inspector,"Column",compact ? 0 : 1);
                Set(inspector,"Width",compact ? Math.Min(320, Math.Max(200, _root.Bounds.Width)) : double.NaN);
                Set(inspector,"HorizontalAlignment",compact ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Stretch);
                Set(inspector,"Layer",compact ? 5 : 0);
            }
        }
        finally { _updating = false; }
    }
    private static IEnumerable<Control> AuthoredControls(Control root)
    {
        yield return root;
        var children = root is CuiContainerControl container ? container.AuthoredChildren : root.GetLogicalChildren().OfType<Control>();
        foreach (var child in children)
            foreach (var descendant in AuthoredControls(child)) yield return descendant;
    }
    private static void Set(Control control, string property, object value)
    {
        if (CuiLayoutProperties.SetLiteral(control,property,Convert.ToString(value,CultureInfo.InvariantCulture) ?? "",CuiPropertyLevel.Instance,"Wave responsive state")) return;
        if (!CuiPropertyPresentation.Set(control,property,value,CuiPropertyLevel.Instance,"Wave responsive state"))
            throw new InvalidOperationException($"The canonical CUI property '{property}' is unavailable for Wave.");
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        void Update()
        {
            if (_disposed) return;
            if (args.PropertyName != "Transport" && !_session.IsBusy) RefreshPeaks();
            ApplyLayout(); InvalidateVisual();
        }
        if (Dispatcher.UIThread.CheckAccess()) Update(); else Dispatcher.UIThread.Post(Update);
    }
    private void RefreshPeaks()
    {
        var project = _session.Project;
        MinHeight = Math.Max(360, RulerHeight + (project?.Tracks.Count ?? 0) * RowHeight + 40);
        var clips = project?.Tracks.SelectMany(t => t.Clips).ToArray() ?? [];
        foreach (var removed in _peaks.Keys.Where(id => !clips.Any(c => c.ClipId == id)).ToArray()) _peaks.Remove(removed);
        foreach (var clip in clips.Take(128))
        {
            var key = $"{clip.SourceSha256}:{clip.SourceStartFrame}:{clip.FrameCount}";
            if (_peaks.TryGetValue(clip.ClipId, out var cached) && cached.Key == key) continue;
            var shared = _peaks.Values.FirstOrDefault(value => value.Key == key);
            if (shared.Data is not null) { _peaks[clip.ClipId] = shared; continue; }
            try
            {
                using (var source = File.OpenRead(clip.SourcePath))
                    if (!string.Equals(Convert.ToHexString(SHA256.HashData(source)), clip.SourceSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The original audio source changed.");
                _peaks[clip.ClipId] = (key, PcmWaveformReader.AnalyzeRange(clip.SourcePath, clip.SourceStartFrame, clip.FrameCount, 256));
            }
            catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException or OverflowException)
            {
                _peaks[clip.ClipId] = (key, null);
                Console.Error.WriteLine($"WaveTimelineAnalysisFailed: {error.GetType().Name}");
            }
        }
    }
    private IBrush Brush(string key, string fallback) => this.TryFindResource(key, out var value) && value is IBrush brush ? brush : new SolidColorBrush(Color.Parse(fallback));
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var project = _session.Project;
        var text = Brush("CuiTextBrush", "#E7E9EC"); var soft = Brush("CuiTextSoftBrush", "#ADB4BE");
        var panel = Brush("CuiPanelBrush", "#2A3039"); var background = Brush("CuiBackgroundBrush", "#1B2027");
        var line = Brush("CuiLineBrush", "#424954"); var accent = Brush("CuiAccentBrush", "#53BDD0");
        var selection = Brush("CuiSelectionBrush", "#294650");
        var typeface = new Typeface(GetValue(TextBlock.FontFamilyProperty));
        void Text(string value, double x, double y, IBrush? colour = null, double size = 12, double width = double.PositiveInfinity)
        {
            var formatted = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, colour ?? text);
            formatted.MaxTextWidth = width; formatted.MaxLineCount = 1; formatted.Trimming = TextTrimming.CharacterEllipsis;
            context.DrawText(formatted, new(x, y));
        }
        context.DrawRectangle(background, null, new Rect(Bounds.Size));
        context.DrawRectangle(panel, null, new Rect(0, 0, Bounds.Width, RulerHeight));
        context.DrawRectangle(panel, null, new Rect(0, RulerHeight, LabelWidth, Math.Max(0, Bounds.Height - RulerHeight)));
        Text("Tracks", 14, 9, soft);
        var interval = Math.Pow(10, Math.Floor(Math.Log10(DisplaySeconds / 10)));
        if (DisplaySeconds / interval > 20) interval *= 5;
        for (var tick = 0; tick < 21 && tick * interval < DisplaySeconds; tick++)
        {
            var seconds = tick * interval;
            var x = LabelWidth + seconds * PixelsPerSecond;
            context.DrawLine(new Pen(line, .5), new(x, RulerHeight), new(x, Bounds.Height));
            Text($"{Math.Floor(seconds / 60):0}:{seconds % 60:00}", x + 4, 9, soft, 11);
        }
        if (project is null) return;
        for (var index = 0; index < project.Tracks.Count; index++)
        {
            var track = project.Tracks[index]; var top = RulerHeight + index * RowHeight;
            if (track.TrackId == _session.SelectedTrackId) context.DrawRectangle(selection, null, new Rect(0, top, LabelWidth, RowHeight));
            Text(track.Name, 14, top + 14, size: 13, width: LabelWidth - 26);
            Text(track.Mute ? "Muted" : track.Solo ? "Solo" : $"{track.Clips.Count} clips", 14, top + 42, soft, 11, LabelWidth - 26);
            Text($"Gain {track.Gain:0.##} · Pan {track.Pan:0.##}", 14, top + 66, soft, 10, LabelWidth - 26);
            context.DrawLine(new Pen(line, 1), new(0, top + RowHeight), new(Bounds.Width, top + RowHeight));
            foreach (var clip in track.Clips)
            {
                var offset = _drag?.Clip == clip.ClipId ? _dragOffset : 0;
                var x = LabelWidth + clip.TimelineStartFrame / (double)project.SampleRate * PixelsPerSecond + offset;
                var width = Math.Max(4, clip.FrameCount / (double)project.SampleRate * PixelsPerSecond);
                var bounds = new Rect(x, top + 8, width, RowHeight - 16);
                using var withinTimeline = context.PushClip(new Rect(LabelWidth, RulerHeight, Math.Max(0, Bounds.Width - LabelWidth), Math.Max(0, Bounds.Height - RulerHeight)));
                context.DrawRectangle(selection, new Pen(accent, _session.SelectedClipId == clip.ClipId ? 2 : .6), bounds, 4, 4);
                using var inside = context.PushClip(bounds.Deflate(3));
                Text(_session.ClipName(clip), x + 8, top + 13, size: 11, width: Math.Max(1, width - 16));
                if (_peaks.TryGetValue(clip.ClipId, out var samples) && samples.Data is { } envelope)
                {
                    var channelHeight = 56d / envelope.Channels.Count;
                    for (var channel = 0; channel < envelope.Channels.Count; channel++)
                    {
                        var center = top + 35 + channelHeight * (channel + .5); var data = envelope.Channels[channel];
                        for (var bucket = 0; bucket < envelope.BucketCount; bucket++)
                        {
                            var at = x + (bucket + .5) / envelope.BucketCount * width;
                            context.DrawLine(new Pen(accent, Math.Max(.5, width / envelope.BucketCount)),
                                new(at, center - data.Maximum[bucket] * channelHeight * .45), new(at, center - data.Minimum[bucket] * channelHeight * .45));
                        }
                    }
                }
                else Text("Waveform unavailable", x + 8, top + 48, soft, 11);
            }
        }
        foreach (var marker in project.Markers)
        {
            var x = LabelWidth + marker.Frame / (double)project.SampleRate * PixelsPerSecond;
            context.DrawLine(new Pen(accent, 1), new(x, 0), new(x, Bounds.Height)); Text(marker.Name, x + 4, RulerHeight + 4, accent, 10);
        }
        var playhead = LabelWidth + _session.Position * PixelsPerSecond;
        context.DrawLine(new Pen(text, 1.5), new(playhead, 0), new(playhead, Bounds.Height));
        context.DrawRectangle(text, null, new Rect(playhead - 4, RulerHeight - 6, 8, 6));
    }
    protected override async void OnPointerPressed(PointerPressedEventArgs args)
    {
        base.OnPointerPressed(args); if (_session.IsBusy || _session.Project is not { } project || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = args.GetPosition(this); Focus();
        if (point.Y < RulerHeight && point.X >= LabelWidth) { await _session.SeekTimelineAsync((point.X - LabelWidth) / PixelsPerSecond); args.Handled = true; return; }
        if (_session.HasPendingInput) return;
        var track = project.Tracks.ElementAtOrDefault((int)((point.Y - RulerHeight) / RowHeight)); if (track is null) return;
        var frame = Math.Max(0, (point.X - LabelWidth) / PixelsPerSecond * project.SampleRate);
        var clip = point.X < LabelWidth ? null : track.Clips.LastOrDefault(c => c.TimelineStartFrame <= frame && frame < c.TimelineStartFrame + c.FrameCount);
        _session.SelectTimelineClip(track.TrackId, clip?.ClipId);
        if (clip is not null) { _drag = (clip.ClipId, clip.TimelineStartFrame, point.X); _dragOffset = 0; args.Pointer.Capture(this); }
        args.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs args)
    {
        base.OnPointerMoved(args); if (_drag is not { } drag || _session.Project is not { } project) return;
        _dragOffset = Math.Max(-drag.OriginalFrame / (double)project.SampleRate * PixelsPerSecond, args.GetPosition(this).X - drag.PointerX);
        InvalidateVisual(); args.Handled = true;
    }
    protected override async void OnPointerReleased(PointerReleasedEventArgs args)
    {
        base.OnPointerReleased(args); if (_drag is not { } drag || _session.Project is not { } project) return;
        var offset = _dragOffset; _drag = null; _dragOffset = 0; args.Pointer.Capture(null);
        if (Math.Abs(offset) >= 3)
        {
            var requested = Math.Max(0, drag.OriginalFrame + Math.Round(offset / PixelsPerSecond * project.SampleRate));
            if (double.IsFinite(requested) && requested < long.MaxValue - 2048d)
            {
                await _session.EditAsync(p => WaveProjectEdits.Move(p, p.Revision, drag.Clip, (long)requested));
                if (_session.SelectedTrackId is { } track) _session.SelectTimelineClip(track, drag.Clip);
            }
        }
        InvalidateVisual(); args.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs args)
    { base.OnPointerCaptureLost(args); _drag = null; _dragOffset = 0; InvalidateVisual(); }
    protected override async void OnKeyDown(KeyEventArgs args)
    {
        base.OnKeyDown(args); if (_session.IsBusy || _session.Project is not { } project) return;
        if (args.Key is Key.Left or Key.Right) { await _session.SeekTimelineAsync(_session.Position + (args.Key == Key.Left ? -1 : 1) * (args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? .1 : 1)); args.Handled = true; }
        else if (args.Key is Key.Up or Key.Down)
        {
            var index = project.Tracks.FindIndex(t => t.TrackId == _session.SelectedTrackId);
            index = Math.Clamp(index + (args.Key == Key.Up ? -1 : 1), 0, project.Tracks.Count - 1);
            _session.SelectTimelineClip(project.Tracks[index].TrackId, null); args.Handled = true;
        }
        else if (args.Key == Key.Delete && _session.SelectedClipId is not null) { await _session.DispatchAsync("Delete", null); args.Handled = true; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _session.PropertyChanged -= Changed; AttachedToVisualTree -= Attached;
        if (_root is not null) _root.SizeChanged -= Resized; _peaks.Clear(); _drag = null;
    }
}
