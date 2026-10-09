using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;

namespace HavenOS.Apps.Wave;

/// <summary>The installed host supplies its SAME Home readiness, project publisher
/// and authorised Files/media services. No app-private authority is started.</summary>
public static class WaveNativeSurface
{
    public static CuiNativeScene CreateScene(WaveCuiWorkspace workspace, ICuiSceneReadiness originalReadiness, CuiAppearance? appearance = null)
    {
        ArgumentNullException.ThrowIfNull(workspace); ArgumentNullException.ThrowIfNull(originalReadiness);
        var registry = new CuiControlRegistry(); registry.RegisterObjectRenderer("wave.timeline", _ => new WaveTimelineControl(workspace));
        return new("wave", "Wave", "Imagine", WaveCuiWorkspace.LoadDocument(), workspace, workspace, originalReadiness)
            { ControlRegistry = registry, Appearance = appearance, IsPublicationCurrent = () => workspace.IsOriginalPublicationCurrent };
    }
    public static WaveNativeWindow CreateWindow(WaveCuiWorkspace workspace, ICuiSceneReadiness originalReadiness, CuiAppearance? appearance = null)
        => new(workspace, CreateScene(workspace, originalReadiness, appearance));
    public static int RunSetupRequired(string[] args)
    {
        var model = new CuiViewModel();
        var scene = new CuiNativeScene("wave", "Wave", "Imagine", WaveCuiWorkspace.LoadDocument(), model, model, new RequiredHome());
        return CuiNativeHost.Run(scene, args);
    }
    private sealed class RequiredHome : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable,
                "HomeStartupAttachmentUnavailable", "Open Wave from Home to connect the authorised project and Files audio services. Install Home if it is missing. Existing audio and projects are preserved."));
        }
    }
}

/// <summary>Actual native window owns initialization, queued edit sources and
/// scene retirement. Failed or canceled originals stay held by this same owner.</summary>
public sealed class WaveNativeWindow : Window
{
    private readonly WaveCuiWorkspace _workspace;
    private readonly CuiNativeScene _originalScene;
    private readonly CuiSceneHost _scene;
    private Task<CuiSceneAvailability>? _initialization;
    private Task? _originalClose, _originalSceneClose, _originalWorkspaceClose;
    private bool _allowClose, _confirmedLeave;
    public CuiSceneHost SceneHost => _scene;
    public Task? OriginalInitialization => _initialization;
    public Task? OriginalClose => _originalClose;
    internal WaveNativeWindow(WaveCuiWorkspace workspace, CuiNativeScene originalScene)
    {
        _workspace = workspace; _originalScene = originalScene; _scene = new(originalScene.ControlRegistry);
        Content = _scene; Title = "Wave"; Width = 1180; Height = 800; MinWidth = 360; MinHeight = 520;
        _workspace.CloseAccepted += OnCloseAccepted;
        Closing += OnClosing;
        SizeChanged += (_, _) => _workspace.SetWidth(Bounds.Width);
        KeyDown += OnKeyDown;
    }
    private void OnCloseAccepted(object? sender, EventArgs args) { _confirmedLeave = true; Close(); }
    public Task<CuiSceneAvailability> InitializeAsync(CancellationToken token = default)
    {
        if (_initialization is not null) return _initialization;
        if (_originalClose is not null || _allowClose) throw new InvalidOperationException("Wave initialization cannot start after retirement.");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _initialization = InitializeCoreAsync(start.Task, token); start.SetResult(); return _initialization;
    }
    private async Task<CuiSceneAvailability> InitializeCoreAsync(Task start, CancellationToken token)
    {
        await start;
        _workspace.SetWidth(Width);
        var availability = await _scene.ShowAsync(_originalScene, token);
        if (_scene.TryFindResource("CuiBackgroundBrush", out var value) && value is IBrush brush) Background = brush;
        return availability;
    }
    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        try { _workspace.DemandExternalPreviewJoin(); _workspace.DemandExternalWaveformJoin(); }
        catch (Exception failure) { System.Diagnostics.Trace.TraceError("Wave refused its original waveform callback self-join: {0}", failure); return; }
        if (_originalClose is not null) return;
        if (!_confirmedLeave && !_workspace.RequestClose()) return;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalInitialization = _initialization; var originals = _workspace.OriginalCommands.ToArray();
        _originalClose = CloseCoreAsync(start.Task, originalInitialization, originals); start.SetResult();
        try { await _originalClose; }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Wave retained its original failed retirement: {0}", error); }
    }
    private async Task CloseCoreAsync(Task start, Task? originalInitialization, IReadOnlyList<Task> originals)
    {
        await start; var failures = new List<Exception>();
        async Task Join(Task? task) { if (task is null) return; try { await task; } catch (Exception error) { failures.Add(error); } }
        try { _workspace.WithdrawForClose(); } catch (Exception error) { failures.Add(error); }
        // Retire the SAME scene first so held readiness can receive its real
        // owner-issued withdrawal. It never waives an original cancellation.
        try { _originalSceneClose = _scene.CloseOriginalAsync(); } catch (Exception error) { failures.Add(error); }
        await Join(originalInitialization); foreach (var original in originals) await Join(original);
        // Every actual close is attempted, even after another source failed.
        try { _originalWorkspaceClose = _workspace.DisposeAsync().AsTask(); } catch (Exception error) { failures.Add(error); }
        await Join(_originalWorkspaceClose); await Join(_originalSceneClose);
        if (failures.Count != 0) throw new AggregateException("Wave retained its original initialization, action or close failure.", failures);
        _workspace.CloseAccepted -= OnCloseAccepted; _allowClose = true; Close();
    }
    private async void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (!args.KeyModifiers.HasFlag(KeyModifiers.Control) || _originalClose is not null ||
            _initialization is not { IsCompletedSuccessfully: true } || _initialization.Result.State != CuiSceneAvailabilityState.Ready) return;
        var command = args.Key switch { Key.S => "9to1.Wave.Save", Key.Z => args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "9to1.Wave.Redo" : "9to1.Wave.Undo", Key.Y => "9to1.Wave.Redo", _ => null };
        if (command is null || _workspace.IsActionAvailable(command) != true) return;
        args.Handled = true;
        try { await _workspace.DispatchAsync(command, null); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Wave retained its original keyboard command: {0}", error); }
    }
}

/// <summary>Specialised CUI Object projects actual sample-frame clip ranges.
/// It does not fabricate waveform samples or simulate a playback backend.</summary>
internal sealed class WaveTimelineControl : Control
{
    private const double LabelWidth = 116, RowHeight = 46;
    private readonly WaveCuiWorkspace _workspace;
    public WaveTimelineControl(WaveCuiWorkspace workspace)
    {
        _workspace = workspace; Focusable = true;
        AttachedToVisualTree += (_, _) => { _workspace.PropertyChanged += Changed; _workspace.SynchronizeOriginalWaveforms(); };
        DetachedFromVisualTree += (_, _) => _workspace.PropertyChanged -= Changed;
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => InvalidateVisual();
    private IBrush Brush(string key) => this.TryFindResource(key, out var value) && value is IBrush brush ? brush : Brushes.Transparent;
    public override void Render(DrawingContext context)
    {
        base.Render(context); var project = _workspace.Project;
        var width = Math.Max(1, Bounds.Width - LabelWidth); var duration = Math.Max(1, _workspace.Duration);
        var foreground = Brush("CuiTextBrush"); var accent = Brush("CuiAccentBrush"); var panel = Brush("CuiPanelBrush"); var clipFill = Brush("CuiAccentSoftBrush");
        var typeface = new Typeface(GetValue(TextBlock.FontFamilyProperty), FontStyle.Normal, FontWeight.Medium);
        void Text(string value, double x, double y) => context.DrawText(new FormattedText(value, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, 12, foreground), new(x, y));
        for (var index = 0; index < project.Tracks.Count; index++)
        {
            var track = project.Tracks[index]; var top = index * RowHeight;
            Text(track.Name.Length > 14 ? track.Name[..14] + "…" : track.Name, 0, top + 8);
            context.DrawRectangle(panel, new Pen(foreground, .5), new Rect(LabelWidth, top + 2, width, RowHeight - 4));
            foreach (var clip in track.Clips)
            {
                var x = LabelWidth + (double)clip.TimelineStartFrame / duration * width;
                var clipWidth = Math.Max(2, (double)clip.FrameCount / duration * width);
                context.DrawRectangle(clipFill, new Pen(accent, clip.ClipId == _workspace.SelectedClipId ? 3 : 1), new Rect(x, top + 5, clipWidth, RowHeight - 10));
                using var originalClipBounds = context.PushClip(new Rect(x, top + 5, clipWidth, RowHeight - 10));
                if (_workspace.ObserveOriginalWaveform(clip) is { } waveform)
                {
                    // Each original channel has its own envelope; the display
                    // projects source range only, preserving project/source bytes.
                    var channelHeight = (RowHeight - 14d) / waveform.Channels.Count;
                    for (var channel = 0; channel < waveform.Channels.Count; channel++)
                    {
                        var center = top + 7 + channelHeight * (channel + .5);
                        var data = waveform.Channels[channel];
                        for (var bucket = 0; bucket < waveform.BucketCount; bucket++)
                        {
                            var at = x + (bucket + .5) / waveform.BucketCount * clipWidth;
                            context.DrawLine(new Pen(foreground, Math.Max(.5, clipWidth / waveform.BucketCount)),
                                new(at, center - data.Maximum[bucket] * channelHeight * .45),
                                new(at, center - data.Minimum[bucket] * channelHeight * .45));
                        }
                    }
                }
                else if (clipWidth > 100) Text(_workspace.ObserveOriginalWaveformStatus(clip), x + 5, top + 13);
            }
        }
        foreach (var marker in project.Markers)
        {
            var x = LabelWidth + (double)marker.Frame / duration * width;
            context.DrawLine(new Pen(foreground, 1), new(x, 0), new(x, Bounds.Height));
        }
        var playhead = LabelWidth + (double)_workspace.Playhead / duration * width;
        context.DrawLine(new Pen(foreground, 2), new(playhead, 0), new(playhead, Bounds.Height));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs args)
    {
        base.OnPointerPressed(args); var point = args.GetPosition(this);
        if (point.X < LabelWidth) return;
        var project = _workspace.Project; var track = project.Tracks.ElementAtOrDefault((int)(point.Y / RowHeight));
        if (track is null) return;
        var frame = (long)Math.Clamp(Math.Round((point.X - LabelWidth) / Math.Max(1, Bounds.Width - LabelWidth) * _workspace.Duration), 0, _workspace.Duration);
        var clip = track.Clips.LastOrDefault(item => item.TimelineStartFrame <= frame && frame < item.TimelineStartFrame + item.FrameCount);
        _workspace.Select(track.TrackId, clip?.ClipId, frame); Focus(); args.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs args)
    {
        base.OnKeyDown(args);
        if (args.Key is Key.Left or Key.Right)
        {
            var project = _workspace.Project;
            var track = project.Tracks.FirstOrDefault(item => item.Clips.Any(clip => clip.ClipId == _workspace.SelectedClipId)) ?? project.Tracks[0];
            _workspace.Select(track.TrackId, _workspace.SelectedClipId, Math.Max(0, _workspace.Playhead + (args.Key == Key.Left ? -1 : 1)));
            args.Handled = true;
        }
    }
}
