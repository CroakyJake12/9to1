using Haven.Core.Media;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Wave;

/// <summary>Native controls and graphical timeline act on the SAME Wave project
/// session. Host capabilities remain the actual original supplier's authority.</summary>
public sealed partial class WaveCuiWorkspace : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged, IAsyncDisposable
{
    private readonly WaveEditSession _session;
    private readonly Func<string, bool> _available;
    private readonly WaveFilesProjectService? _files;
    private readonly Func<CancellationToken, Task<(string FileId, string? Revision)?>>? _pickAsset;
    private readonly Func<CancellationToken, Task<string?>>? _exportTarget;
    private readonly Dictionary<string, string> _drafts = new(StringComparer.Ordinal)
    {
        ["ClipStart"] = "0", ["TrimStart"] = "0", ["TrimEnd"] = "0", ["ClipGain"] = "1",
        ["FadeIn"] = "0", ["FadeOut"] = "0", ["TrackGain"] = "1", ["TrackPan"] = "0",
        ["TrackName"] = "Audio", ["MarkerName"] = "Marker", ["RegionName"] = "Region",
        ["RegionStart"] = "0", ["RegionDuration"] = "48000"
    };
    private readonly Dictionary<string, string> _baselines = new(StringComparer.Ordinal);
    private readonly List<Task> _originalCommands = [], _originalExternalSources = [];
    private readonly object _originalExternalGate = new();
    private static readonly AsyncLocal<WaveCuiWorkspace?> LogicalDriver = new();
    private WaveProject _snapshot;
    private Guid _trackId;
    private Guid? _clipId;
    private bool _compact, _mute, _solo, _baselineMute, _baselineSolo, _busy, _closeRequested;
    private volatile bool _retiring, _disposed;
    private long _playhead;
    private string _status = "Open a clip to edit its ranges, fades and mix. Save keeps the native project.";
    private Task? _originalCommand, _originalClose;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? CloseAccepted;
    public WaveProject Project => _session.Project;
    public bool IsOriginalPublicationCurrent => !_retiring && !_disposed;
    public Guid? SelectedClipId => _clipId;
    public long Playhead => _playhead;
    public bool HasPendingDrafts => _mute != _baselineMute || _solo != _baselineSolo || _drafts.Any(pair => _baselines.GetValueOrDefault(pair.Key) != pair.Value);
    public bool HasUnsavedChanges => _session.IsDirty || HasPendingDrafts;
    public Task? OriginalCommand => _originalCommand;
    public IReadOnlyList<Task> OriginalExternalSources { get { lock (_originalExternalGate) return _originalExternalSources.ToArray(); } }
    public Task? OriginalClose => _originalClose;
    public IReadOnlyList<Task> OriginalCommands => _originalCommands.AsReadOnly();
    private WaveTrack Track => _snapshot.Tracks.Single(track => track.TrackId == _trackId);
    private WaveClip? Clip => Track.Clips.SingleOrDefault(clip => clip.ClipId == _clipId);

    public WaveCuiWorkspace(WaveEditSession session, Func<string, bool> originalAvailability,
        WaveFilesProjectService? files = null,
        Func<CancellationToken, Task<(string FileId, string? Revision)?>>? pickAsset = null,
        Func<CancellationToken, Task<string?>>? exportTarget = null, IMediaEngine? originalMediaEngine = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _available = originalAvailability ?? throw new ArgumentNullException(nameof(originalAvailability));
        _files = files; _pickAsset = pickAsset; _exportTarget = exportTarget; _originalMediaEngine = originalMediaEngine;
        _previewTimer.Tick += OnOriginalPreviewTick;
        _snapshot = session.Project;
        if (_snapshot.Tracks.Count == 0) throw new InvalidDataException("A native Wave workspace needs an existing audio track.");
        _trackId = _snapshot.Tracks[0].TrackId; LoadFields();
    }
    public static CuiDocument LoadDocument()
    {
        using var source = typeof(WaveCuiWorkspace).Assembly.GetManifestResourceStream("HavenOS.Wave.UI.WaveWorkspace.cui")
            ?? throw new InvalidDataException("The Wave CUI source is missing.");
        using var reader = new StreamReader(source); var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "WaveWorkspace.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return document;
    }
    public bool TryGetValue(string path, out object? value)
    {
        if (TryGetSourcePreviewValue(path, out value)) return true;
        if (_drafts.TryGetValue(path, out var draft)) { value = draft; return true; }
        value = path switch
        {
            "ProjectLabel" => $"Wave · revision {_snapshot.Revision}{(_session.IsDirty ? " · unsaved" : "")}",
            "AudioConfiguration" => $"{_snapshot.SampleRate:N0} Hz · {_snapshot.Channels} channels",
            "TrackNames" => _snapshot.Tracks.Select(track => track.Name + (track.Mute ? " · muted" : "") + (track.Solo ? " · solo" : "")).ToArray(),
            "TrackIndex" => _snapshot.Tracks.FindIndex(track => track.TrackId == _trackId),
            "ClipNames" => Track.Clips.Select(clip => $"{Time(clip.TimelineStartFrame)}–{Time(clip.TimelineStartFrame + clip.FrameCount)} · {clip.ClipId.ToString()[..8]}").ToArray(),
            "ClipIndex" => Track.Clips.FindIndex(clip => clip.ClipId == _clipId),
            "Selection" => Clip is { } clip ? $"{Track.Name} · source frames {clip.SourceStartFrame}–{clip.SourceStartFrame + clip.FrameCount}" : "Select a clip to edit.",
            "TrackMute" => _mute, "TrackSolo" => _solo,
            "PlayheadFrame" => _playhead.ToString(CultureInfo.InvariantCulture),
            "PlayheadText" => Time(_playhead) + $" · frame {_playhead}",
            "MarkerNames" => _snapshot.Markers.Select(marker => $"{Time(marker.Frame)} · {marker.Name}").ToArray(),
            "RegionNames" => _snapshot.Regions.Select(region => $"{Time(region.StartFrame)}–{Time(region.StartFrame + region.FrameCount)} · {region.Name}").ToArray(),
            "Status" => _status,
            "DraftStatus" => HasPendingDrafts ? "Pending inspector fields are retained. Apply the matching edit or discard fields before changing selection." : "No pending inspector fields.",
            "HasPendingDrafts" => HasPendingDrafts, "CloseRequested" => _closeRequested,
            "CanSave" => IsActionAvailable("9to1.Wave.Save"), "CanUndo" => IsActionAvailable("9to1.Wave.Undo"),
            "CanRedo" => IsActionAvailable("9to1.Wave.Redo"), "CanEditClip" => IsActionAvailable("9to1.Wave.Move"),
            "CanEdit" => !_busy && !_retiring && !_disposed,
            "CanImport" => IsActionAvailable("9to1.Wave.Import"), "CanExport" => IsActionAvailable("9to1.Wave.Export"),
            "WorkspaceColumns" => _compact ? "*" : "220,*,270", "WorkspaceRows" => _compact ? "Auto,Auto,Auto" : "Auto",
            "TimelineColumn" => _compact ? 0 : 1, "TimelineRow" => _compact ? 1 : 0,
            "InspectorColumn" => _compact ? 0 : 2, "InspectorRow" => _compact ? 2 : 0,
            "TimelineHeight" => Math.Max(160, _snapshot.Tracks.Count * 46 + 24),
            "SourceStatus" => _files is null ? "Open Wave from Home to connect authorised Files audio sources and export." : "Audio assets stay in Files; edits preserve their original bytes.",
            _ => null
        };
        return value is not null;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (_busy || _retiring || _disposed) return false;
        if (_drafts.ContainsKey(path) && value is string text && text.Length <= 512) _drafts[path] = text;
        else if (path == "PlayheadFrame" && value is string frameText && long.TryParse(frameText, NumberStyles.None, CultureInfo.InvariantCulture, out var frame)) _playhead = frame;
        else if (path is "TrackMute" or "TrackSolo" && value is bool flag)
        { if (path == "TrackMute") _mute = flag; else _solo = flag; }
        else if (path is "TrackIndex" or "ClipIndex" && value is int index && index >= 0)
        {
            if (HasPendingDrafts) { _status = "Apply or discard pending inspector fields first."; Changed(); return false; }
            if (path == "TrackIndex" && index < _snapshot.Tracks.Count)
            { _trackId = _snapshot.Tracks[index].TrackId; _clipId = null; LoadFields(); }
            else if (path == "ClipIndex" && index < Track.Clips.Count)
            { Select(_trackId, Track.Clips[index].ClipId, Track.Clips[index].TimelineStartFrame); return true; }
            else return false;
        }
        else return false;
        Changed(); return true;
    }
    public void Select(Guid trackId, Guid? clipId, long frame)
    {
        if (_busy || _retiring || _disposed || frame < 0) return;
        if (!_snapshot.Tracks.Any(track => track.TrackId == trackId && (clipId is null || track.Clips.Any(clip => clip.ClipId == clipId)))) return;
        if ((_trackId != trackId || _clipId != clipId) && HasPendingDrafts)
        { _status = "Apply or discard pending inspector fields before selecting another clip."; Changed(); return; }
        var selectionChanged = _trackId != trackId || _clipId != clipId;
        _trackId = trackId; _clipId = clipId; _playhead = frame;
        if (selectionChanged) LoadFields();
        Changed();
    }
    public bool? IsActionAvailable(string command)
    {
        if (_busy || _retiring || _disposed || !_available(command) || _retiring || _disposed) return false;
        if (HasPendingDrafts && command is "9to1.Wave.Undo" or "9to1.Wave.Redo" or "9to1.Wave.Delete" or "9to1.Wave.RippleDelete") return false;
        return command switch
        {
            "9to1.Wave.Undo" => _session.CanUndo, "9to1.Wave.Redo" => _session.CanRedo,
            "9to1.Wave.Save" => !HasPendingDrafts, "9to1.Wave.SaveAndClose" => _closeRequested && !HasPendingDrafts,
            "9to1.Wave.DiscardAndClose" or "9to1.Wave.KeepEditing" => _closeRequested,
            "9to1.Wave.DiscardDrafts" => HasPendingDrafts,
            "9to1.Wave.SourcePlay" => CanPlayOriginalSource,
            "9to1.Wave.SourcePause" or "9to1.Wave.SourceStop" => _sourcePreview?.HasNativeSession == true,
            "9to1.Wave.SourceSeek" => CurrentOriginalPreviewMatchesSelection(),
            "9to1.Wave.Import" => _files is not null && _pickAsset is not null,
            "9to1.Wave.Export" => _files is not null && _exportTarget is not null && _snapshot.Tracks.Any(track => track.Clips.Count > 0),
            "9to1.Wave.Move" or "9to1.Wave.Trim" or "9to1.Wave.Split" or "9to1.Wave.Duplicate" or "9to1.Wave.Delete" or "9to1.Wave.RippleDelete" or "9to1.Wave.Processing" or "9to1.Wave.Join" => Clip is not null,
            "9to1.Wave.AddTrack" or "9to1.Wave.Mixer" or "9to1.Wave.AddMarker" or "9to1.Wave.AddRegion" or
                "9to1.Wave.Start" or "9to1.Wave.End" or "9to1.Wave.PreviousMarker" or "9to1.Wave.NextMarker" => true,
            _ => false
        };
    }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default)
    {
        Dispatcher.UIThread.VerifyAccess(); token.ThrowIfCancellationRequested();
        if (IsActionAvailable(command) != true) return ValueTask.CompletedTask;
        _originalCommands.RemoveAll(task => task.IsCompletedSuccessfully);
        if (_originalCommands.Count >= 128) throw new InvalidOperationException("Wave retains unresolved command failures. Close or recover this workspace before more work.");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _busy = true; var original = RunAsync(start.Task, command, token);
        _originalCommand = original; _originalCommands.Add(original);
        Exception? notificationFailure = null;
        try { Changed(); } catch (Exception error) { notificationFailure = error; }
        finally { if (notificationFailure is null) start.SetResult(); else start.SetException(notificationFailure); }
        return new(original);
    }
    private async Task RunAsync(Task start, string command, CancellationToken token)
    {
        var previous = LogicalDriver.Value; LogicalDriver.Value = this; var failures = new List<Exception>();
        try
        {
            await start; token.ThrowIfCancellationRequested();
            switch (command)
            {
                case "9to1.Wave.SourcePlay": await PlayOriginalSourceAsync(token); break;
                case "9to1.Wave.SourcePause": await PauseOriginalSourceAsync(token); break;
                case "9to1.Wave.SourceStop": await StopOriginalSourceAsync(); break;
                case "9to1.Wave.SourceSeek": await SeekOriginalSourceAsync(token); break;
                case "9to1.Wave.Save": await _session.SaveAsync(token); _status = "Native project saved."; break;
                case "9to1.Wave.SaveAndClose": await _session.SaveAsync(token); _closeRequested = false; CloseAccepted?.Invoke(this, EventArgs.Empty); break;
                case "9to1.Wave.DiscardAndClose": _closeRequested = false; CloseAccepted?.Invoke(this, EventArgs.Empty); break;
                case "9to1.Wave.KeepEditing": _closeRequested = false; break;
                case "9to1.Wave.DiscardDrafts": LoadFields(); _status = "Pending inspector fields discarded; accepted edits retained."; break;
                case "9to1.Wave.Undo": _session.Undo(); RefreshSnapshot(); LoadFields(); break;
                case "9to1.Wave.Redo": _session.Redo(); RefreshSnapshot(); LoadFields(); break;
                case "9to1.Wave.AddTrack": Edit("Add track", project => WaveProjectEdits.AddTrack(project, project.Revision, _drafts["TrackName"])); Acknowledge("TrackName"); break;
                case "9to1.Wave.Move": ClipEdit("Move clip", (project, clip) => WaveProjectEdits.Move(project, project.Revision, clip.ClipId, Frame("ClipStart")), "ClipStart"); break;
                case "9to1.Wave.Trim": ClipEdit("Trim clip", (project, clip) => WaveProjectEdits.Trim(project, project.Revision, clip.ClipId, Frame("TrimStart"), Frame("TrimEnd")), "TrimStart", "TrimEnd"); break;
                case "9to1.Wave.Split": ClipEdit("Split clip", (project, clip) => WaveProjectEdits.Split(project, project.Revision, clip.ClipId, _playhead)); break;
                case "9to1.Wave.Duplicate": ClipEdit("Duplicate clip", (project, clip) => WaveProjectEdits.Duplicate(project, project.Revision, clip.ClipId, Frame("ClipStart")), "ClipStart"); break;
                case "9to1.Wave.Delete": ClipEdit("Lift clip", (project, clip) => WaveProjectEdits.Delete(project, project.Revision, clip.ClipId, false)); break;
                case "9to1.Wave.RippleDelete": ClipEdit("Ripple delete clip", (project, clip) => WaveProjectEdits.Delete(project, project.Revision, clip.ClipId, true)); break;
                case "9to1.Wave.Join":
                    ClipEdit("Join clips", (project, clip) => WaveProjectEdits.Join(project, project.Revision, clip.ClipId,
                        JoinTarget(clip))); break;
                case "9to1.Wave.Processing": ClipEdit("Clip gain and fades", (project, clip) => WaveProjectEdits.SetClipProcessing(project, project.Revision, clip.ClipId, Number("ClipGain"), Frame("FadeIn"), Frame("FadeOut")), "ClipGain", "FadeIn", "FadeOut"); break;
                case "9to1.Wave.Mixer": Edit("Track mixer", project => WaveProjectEdits.SetTrackMixer(project, project.Revision, _trackId, Number("TrackGain"), Number("TrackPan"), _mute, _solo)); Acknowledge("TrackGain", "TrackPan"); _baselineMute = _mute; _baselineSolo = _solo; break;
                case "9to1.Wave.AddMarker": Edit("Add marker", project => WaveTimelineAnnotations.AddMarker(project, project.Revision, _playhead, _drafts["MarkerName"])); Acknowledge("MarkerName"); break;
                case "9to1.Wave.AddRegion": Edit("Add region", project => WaveTimelineAnnotations.AddRegion(project, project.Revision, Frame("RegionStart"), Frame("RegionDuration"), _drafts["RegionName"])); Acknowledge("RegionStart", "RegionDuration", "RegionName"); break;
                case "9to1.Wave.Start": _playhead = 0; break;
                case "9to1.Wave.End": _playhead = Duration; break;
                case "9to1.Wave.PreviousMarker": _playhead = WaveTimelineAnnotations.Navigate(_snapshot, _playhead, false) ?? _playhead; break;
                case "9to1.Wave.NextMarker": _playhead = WaveTimelineAnnotations.Navigate(_snapshot, _playhead, true) ?? _playhead; break;
                case "9to1.Wave.Import": await ImportAsync(token); break;
                case "9to1.Wave.Export": await ExportAsync(token); break;
                default: throw new NotSupportedException("Wave action is unavailable: " + command);
            }
        }
        catch (LocalEditRefusal refusal)
        { _status = "Edit was not applied: " + refusal.InnerException!.Message; }
        catch (Exception error) { failures.Add(error); _status = "Operation could not finish: " + error.Message; }
        finally
        {
            _busy = false;
            try { Changed(); } catch (Exception error) { failures.Add(error); }
            LogicalDriver.Value = previous;
        }
        if (failures.Count != 0) throw new AggregateException("Wave retains its original command failures.", failures);
    }
    private sealed class LocalEditRefusal(Exception cause) : Exception("Local Wave validation declined the edit before publication.", cause) { }
    private void Edit(string name, Func<WaveProject, WaveProject> edit, bool localValidation = true)
    {
        if (_session.Project.Revision != _snapshot.Revision) throw new InvalidOperationException("RevisionConflict: reopen the current native project.");
        WaveProject candidate;
        try
        {
            // These private callers use the existing pure Wave domain edits.
            // Validate the detached candidate before the shared history changes.
            candidate = edit(_session.Project); WaveProjectStore.Validate(candidate);
        }
        catch (Exception cause) when (localValidation && (cause is ArgumentException or NotSupportedException or KeyNotFoundException))
        { throw new LocalEditRefusal(cause); }
        _session.Apply(name, _ => candidate); RefreshSnapshot();
        _status = name + " applied. Source bytes preserved.";
    }
    private Guid JoinTarget(WaveClip clip)
    {
        var next = Track.Clips.Where(item => item.ClipId != clip.ClipId && item.TimelineStartFrame == clip.TimelineStartFrame + clip.FrameCount).ToArray();
        return next.Length == 1 ? next[0].ClipId : throw new NotSupportedException("Select a clip with one adjacent compatible clip to join.");
    }
    private void ClipEdit(string name, Func<WaveProject, WaveClip, WaveProject> edit, params string[] consumed)
    { var clip = Clip ?? throw new LocalEditRefusal(new ArgumentException("Select a clip first.")); Edit(name, project => edit(project, clip)); Acknowledge(consumed); }
    private void RefreshSnapshot()
    {
        _snapshot = _session.Project;
        if (!_snapshot.Tracks.Any(track => track.TrackId == _trackId)) _trackId = _snapshot.Tracks[0].TrackId;
        if (!Track.Clips.Any(clip => clip.ClipId == _clipId)) _clipId = null;
    }
    private async Task ImportAsync(CancellationToken token)
    {
        var originalPick = CaptureExternal(() => _pickAsset!(token));
        var selected = await originalPick; if (selected is null) return;
        var originalImport = CaptureExternal(() => _files!.ImportAsync(_snapshot, _snapshot.Revision, _trackId, selected.Value.FileId,
            selected.Value.Revision, (double)_playhead / _snapshot.SampleRate, token));
        var result = await originalImport;
        if (!result.IsSuccess) throw new IOException(result.Error!.Message);
        Edit("Import audio from Files", _ => result.Value!, localValidation: false);
    }
    private async Task ExportAsync(CancellationToken token)
    {
        var originalTarget = CaptureExternal(() => _exportTarget!(token));
        var target = await originalTarget; if (target is null) return;
        var originalExport = CaptureExternal(() => _files!.ExportPcm16Async(_snapshot, _snapshot.Revision, target, token));
        var result = await originalExport;
        if (!result.IsSuccess) throw new IOException(result.Error!.Message);
        _status = "Rendered mix exported · " + result.Value + " audio frames. Native project retained.";
    }
    private Task<T> CaptureExternal<T>(Func<Task<T>> producer)
    {
        lock (_originalExternalGate)
        {
        _originalExternalSources.RemoveAll(task => task.IsCompletedSuccessfully);
        if (_originalExternalSources.Count >= 128) throw new InvalidOperationException("Wave retains unresolved external sources. Recover this same workspace before more external work.");
        var original = producer() ?? throw new InvalidOperationException("The external owner did not issue its original source task.");
        _originalExternalSources.Add(original); return original;
        }
    }
    private long Frame(string path) => long.TryParse(_drafts[path], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        ? value : throw new ArgumentException("Enter a non-negative whole audio-frame count for " + path + ".");
    private double Number(string path) => double.TryParse(_drafts[path], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
        ? value : throw new ArgumentException("Enter a finite number for " + path + ".");
    private string Time(long frame)
    {
        var seconds = frame / _snapshot.SampleRate; var milliseconds = frame % _snapshot.SampleRate * 1000 / _snapshot.SampleRate;
        return (seconds / 3600).ToString("D2", CultureInfo.InvariantCulture) + ":" + (seconds / 60 % 60).ToString("D2", CultureInfo.InvariantCulture) +
            ":" + (seconds % 60).ToString("D2", CultureInfo.InvariantCulture) + "." + milliseconds.ToString("D3", CultureInfo.InvariantCulture);
    }
    public void SetWidth(double width) { _compact = width < 900; Changed(); }
    public long Duration => _snapshot.Tracks.SelectMany(track => track.Clips).Select(clip => clip.TimelineStartFrame + clip.FrameCount)
        .Concat(_snapshot.Regions.Select(region => region.StartFrame + region.FrameCount)).Concat(_snapshot.Markers.Select(marker => marker.Frame)).DefaultIfEmpty(_snapshot.SampleRate * 10L).Max();
    private void LoadFields()
    {
        _drafts["TrackGain"] = Track.Gain.ToString("R", CultureInfo.InvariantCulture); _drafts["TrackPan"] = Track.Pan.ToString("R", CultureInfo.InvariantCulture);
        _mute = _baselineMute = Track.Mute; _solo = _baselineSolo = Track.Solo;
        if (Clip is { } clip)
        {
            _drafts["ClipStart"] = clip.TimelineStartFrame.ToString(CultureInfo.InvariantCulture);
            _drafts["ClipGain"] = clip.Gain.ToString("R", CultureInfo.InvariantCulture);
            _drafts["FadeIn"] = clip.FadeInFrames.ToString(CultureInfo.InvariantCulture); _drafts["FadeOut"] = clip.FadeOutFrames.ToString(CultureInfo.InvariantCulture);
        }
        _drafts["TrimStart"] = _drafts["TrimEnd"] = "0"; Acknowledge(_drafts.Keys.ToArray());
    }
    private void Acknowledge(params string[] paths) { foreach (var path in paths) _baselines[path] = _drafts[path]; }
    private void Changed()
    {
        SynchronizeOriginalWaveforms();
        SynchronizeOriginalPreviewSelection();
        WithinOriginalPreview(() => { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); return true; });
    }
    public bool RequestClose()
    {
        DemandExternalPreviewJoin();
        if (_disposed) return true;
        if (_busy || _retiring || _session.IsPublishing) { _status = "The current original operation must finish before closing."; Changed(); return false; }
        if (HasUnsavedChanges) { _closeRequested = true; Changed(); return false; }
        return true;
    }
    internal void WithdrawForClose() { _retiring = true; Changed(); }
    public ValueTask DisposeAsync()
    {
        DemandExternalPreviewJoin();
        DemandExternalWaveformJoin();
        if (_session.IsCurrentSaveOwner || ReferenceEquals(LogicalDriver.Value, this) && _originalCommand is { IsCompleted: false })
            throw new InvalidOperationException("A Wave operation cannot join its own retirement.");
        if (_originalClose is not null) return new(_originalClose);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _retiring = true;
        var originals = _originalCommands.Concat(_session.OriginalSaves).Concat(_session.OriginalPublications).Concat(OriginalExternalSources).Concat(OriginalWaveformSources).Concat(OriginalPreviewSources).Distinct().ToArray(); _originalClose = CloseAsync(start.Task, originals);
        Exception? failure = null; try { Changed(); } catch (Exception error) { failure = error; }
        finally { if (failure is null) start.SetResult(); else start.SetException(failure); }
        return new(_originalClose);
    }
    private async Task CloseAsync(Task start, IReadOnlyList<Task> originals)
    {
        var failures = new List<Exception>();
        try { await start; } catch (Exception error) { failures.Add(error); }
        foreach (var original in originals) try { await original; } catch (Exception error) { failures.Add(error); }
        try { await CloseOriginalPreviewsAsync(); } catch (Exception error) { failures.Add(error); }
        var joined = new HashSet<Task>(originals, ReferenceEqualityComparer.Instance);
        while (true)
        {
            var added = OriginalExternalSources.Concat(OriginalPreviewSources).Where(source => !joined.Contains(source)).Distinct().ToArray();
            if (added.Length == 0) break;
            foreach (var original in added) { joined.Add(original); try { await original; } catch (Exception error) { failures.Add(error); } }
        }
        if (failures.Count != 0) throw new AggregateException("Wave retirement retains failed or canceled original sources.", failures);
        _disposed = true;
    }
}
