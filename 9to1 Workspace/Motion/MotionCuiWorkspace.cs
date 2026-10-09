using System.ComponentModel;
using System.Globalization;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Core.Media;

namespace HavenOS.Apps.Motion;

/// <summary>The CUI workspace and API dispatcher share the same revision-aware editing session.</summary>
public sealed partial class MotionCuiWorkspace : ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged, IAsyncDisposable
{
    private readonly MotionEditSession _session;
    private readonly MotionProjectStore _store = new();
    private readonly Func<string, bool> _available;
    private readonly MotionMediaService? _media;
    private readonly Func<CancellationToken, Task<MotionAssetReference?>>? _pickAsset;
    private readonly Func<CancellationToken, Task<string?>>? _exportTarget;
    private readonly Dictionary<string, string> _drafts = new() { ["Start"] = "0", ["SourceIn"] = "0", ["SourceOut"] = "150", ["RangeStart"] = "0", ["RangeEnd"] = "30", ["CaptionText"] = "", ["SubtitleText"] = "", ["MarkerName"] = "", ["MarkerFrame"] = "0" };
    private Guid _sequenceId, _trackId;
    private Guid? _elementId;
    private int _assetIndex, _captionIndex, _captionTrackIndex, _destinationTrackIndex;
    private long _playhead;
    private bool _busy, _disposed;
    private string _status = "Accepted edits save automatically. Apply or discard pending fields before changing selection.";
    private MotionSourcePlayback? _playback;
    private CancellationTokenSource? _operation;
    public event PropertyChangedEventHandler? PropertyChanged;
    public MotionProject Project => _session.Project;
    public MotionSequence Sequence => Project.Sequences.Single(s => s.SequenceId == _sequenceId);
    public Guid? SelectedElementId => _elementId;
    public long Playhead => _playhead;
    private MotionTrack Track => Sequence.VideoTracks.Single(t => t.TrackId == _trackId);
    private MotionElement? Element => Sequence.VideoTracks.SelectMany(t => t.Elements).SingleOrDefault(e => e.ElementId == _elementId);
    private MotionCaptionTrack? Captions => Sequence.CaptionTracks?.ElementAtOrDefault(_captionTrackIndex);
    private MotionCaptionCue? Caption => Captions?.Cues.ElementAtOrDefault(_captionIndex);
    public MotionCuiWorkspace(MotionEditSession session, Func<string, bool> available, MotionMediaService? media = null,
        Func<CancellationToken, Task<MotionAssetReference?>>? pickAsset = null, Func<CancellationToken, Task<string?>>? exportTarget = null)
    {
        _session = session; _available = available; _media = media; _pickAsset = pickAsset; _exportTarget = exportTarget;
        _sequenceId = Project.Sequences[0].SequenceId; _trackId = Sequence.VideoTracks[0].TrackId; LoadCaption();
    }
    public static CuiDocument LoadDocument()
    {
        using var source = typeof(MotionCuiWorkspace).Assembly.GetManifestResourceStream("HavenOS.Motion.UI.MotionWorkspace.cui") ?? throw new InvalidDataException("Motion CUI source missing.");
        using var reader = new StreamReader(source); var parser = new CuiRichParser(); var document = parser.Parse(reader.ReadToEnd(), "MotionWorkspace.cui");
        if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error)) throw new InvalidDataException(string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return document;
    }
    public bool TryGetValue(string path, out object? value)
    {
        if (_retiring || _disposed) { value = path.StartsWith("Can", StringComparison.Ordinal) || path == "HasUnsavedChanges" ? false : null; return value is not null; }
        if (TryGetMarkerValue(path, out value)) return true;
        if (_drafts.TryGetValue(path, out var draft)) { value = draft; return true; }
        value = path switch
        {
            "HasUnsavedChanges" => HasUnsavedChanges, "CanDiscardDrafts" => !_busy && HasUnsavedChanges,
            "DraftStatus" => HasUnsavedChanges ? "Pending fields are kept locally. Apply the matching clip/caption action or discard them." : "No pending fields.",
            "Status" => _status, "ProjectLabel" => $"Motion · revision {Project.Revision}",
            "SequenceLabel" => $"{Sequence.Width} × {Sequence.Height} · {Sequence.FrameRateNumerator}/{Sequence.FrameRateDenominator} fps",
            "SequenceNames" => Project.Sequences.Select((s,i) => $"Sequence {i+1} · {s.Width} × {s.Height}").ToArray(),
            "SequenceIndex" => Project.Sequences.ToList().FindIndex(s => s.SequenceId == _sequenceId),
            "AssetNames" => Project.AssetReferences.Select((a, i) => $"Source {i + 1}").ToArray(), "AssetIndex" => _assetIndex,
            "TrackNames" => Sequence.VideoTracks.Select(t => $"{t.Name}{(t.Locked ? " · locked" : "")}{(!t.Visible ? " · hidden" : "")}").ToArray(),
            "TrackIndex" => Sequence.VideoTracks.ToList().FindIndex(t => t.TrackId == _trackId),
            "DestinationTrackIndex" => _destinationTrackIndex,
            "ClipNames" => Track.Elements.Select(e => $"{e.TimelineStart}–{e.TimelineStart + e.Duration} · {e.ElementId.ToString()[..8]}").ToArray(),
            "ClipIndex" => Track.Elements.ToList().FindIndex(e => e.ElementId == _elementId),
            "Selection" => Element is { } e ? $"{Track.Name} · timeline {e.TimelineStart}–{e.TimelineStart + e.Duration} · source {e.SourceIn}–{e.SourceOut}" : "No clip selected",
            "PreviewStatus" => _media is null ? "Source preview is not available on this device." : Element is null ? "Select a clip to preview its source." : "Preview plays the original source clip.",
            "ExportStatus" => _media?.CanRender != true ? "Video export is not available on this device." : "Video export currently excludes sound. Export caption text separately.",
            "Playhead" => _playhead.ToString(CultureInfo.InvariantCulture),
            "CaptionTrackNames" => Sequence.CaptionTracks?.Select(t => $"{t.Name} · {t.Language}").ToArray() ?? [],
            "CaptionTrackIndex" => _captionTrackIndex,
            "CaptionNames" => Captions?.Cues.Select(c => $"{c.StartFrame}–{c.EndFrame} · {c.Text.Replace('\n', ' ')}").ToArray() ?? [],
            "CaptionIndex" => _captionIndex, "CanEdit" => !_busy && !_disposed && !Track.Locked,
            "CanUndo" => IsActionAvailable("9to1.Motion.Undo"), "CanRedo" => IsActionAvailable("9to1.Motion.Redo"),
            "CanImport" => IsActionAvailable("9to1.Motion.ImportAsset"), "CanPlay" => IsActionAvailable("9to1.Motion.PlaySource"),
            "CanRender" => IsActionAvailable("9to1.Motion.Render"), "CanCancel" => _operation is not null,
            "CanSelect" => !_busy && !_disposed, _ => null
        };
        return value is not null;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (_busy || _disposed || _retiring) return false;
        if (_drafts.ContainsKey(path) && value is string text && text.Length <= 1_000_000) _drafts[path] = text;
        else if (path == "Playhead" && value is string time && long.TryParse(time, NumberStyles.None, CultureInfo.InvariantCulture, out var frame)) _playhead = frame;
        else if (value is int index && index >= 0)
        {
            if (HasUnsavedChanges && path is "SequenceIndex" or "TrackIndex" or "ClipIndex" or "CaptionTrackIndex" or "CaptionIndex")
            { _status = "Apply or discard pending fields before changing selection."; Changed(); return false; }
            switch (path)
            {
                case "SequenceIndex" when index < Project.Sequences.Count: _markerId = null; _sequenceId = Project.Sequences[index].SequenceId; _trackId = Sequence.VideoTracks[0].TrackId; _elementId = null; _playhead = 0; _destinationTrackIndex = 0; _captionTrackIndex = 0; _captionIndex = 0; LoadCaption(); LoadMarkerDrafts(); break;
                case "AssetIndex" when index < Project.AssetReferences.Count: _assetIndex = index; break;
                case "DestinationTrackIndex" when index < Sequence.VideoTracks.Count: _destinationTrackIndex = index; break;
                case "TrackIndex" when index < Sequence.VideoTracks.Count: _trackId = Sequence.VideoTracks[index].TrackId; _elementId = null; break;
                case "ClipIndex" when index < Track.Elements.Count: Select(Track.Elements[index].ElementId, Track.Elements[index].TimelineStart); return true;
                case "CaptionTrackIndex" when index < (Sequence.CaptionTracks?.Count ?? 0): _captionTrackIndex = index; _captionIndex = 0; LoadCaption(); break;
                case "CaptionIndex" when index < (Captions?.Cues.Count ?? 0): _captionIndex = index; LoadCaption(); break;
                default: return false;
            }
        }
        else return false;
        Changed(); return true;
    }
    public void Select(Guid? elementId, long frame)
    {
        if (_busy || _disposed || _retiring) return;
        if (_elementId == elementId) { _playhead = Math.Max(0, frame); Changed(); return; }
        if (HasUnsavedChanges) { _status = "Apply or discard pending fields before selecting another clip."; Changed(); return; }
        _playhead = Math.Max(0, frame); _elementId = elementId;
        if (Element is { } element)
        {
            _trackId = element.TrackId;
            _drafts["Start"] = element.TimelineStart.ToString(CultureInfo.InvariantCulture);
            _drafts["SourceIn"] = element.SourceIn.ToString(CultureInfo.InvariantCulture);
            _drafts["SourceOut"] = element.SourceOut.ToString(CultureInfo.InvariantCulture);
        }
        AcknowledgeDrafts("Start", "SourceIn", "SourceOut");
        Changed();
    }
    public bool? IsActionAvailable(string command)
    {
        if (_disposed || _retiring) return false;
        using (EnterPhysicalOriginal()) if (!_available(command)) return false;
        if (_disposed || _retiring) return false;
        if (command == "9to1.Motion.Cancel") return _operation is not null;
        if (_busy || !CanRunWithPendingDrafts(command)) return false;
        if (command == "9to1.Motion.DiscardDrafts") return HasUnsavedChanges;
        if (HasUnsavedChanges && command is "9to1.Motion.Reload" or "9to1.Motion.Undo" or "9to1.Motion.Redo" or "9to1.Motion.Delete" or "9to1.Motion.RippleDelete" or "9to1.Motion.SplitCaption" or "9to1.Motion.MergeCaption") return false;
        if (MarkerActionAvailability(command) is { } markerAvailable) return markerAvailable;
        return command switch
        {
            "9to1.Motion.Undo" => _session.CanUndo, "9to1.Motion.Redo" => _session.CanRedo,
            "9to1.Motion.ImportAsset" => _pickAsset is not null,
            "9to1.Motion.RelinkAsset" => _pickAsset is not null && Project.AssetReferences.Count > 0,
            "9to1.Motion.PlaySource" => _media is not null && Element is not null,
            "9to1.Motion.Pause" => _playback is not null,
            "9to1.Motion.Render" => _media?.CanRender == true && _exportTarget is not null,
            "9to1.Motion.Roll" or "9to1.Motion.Slide" or "9to1.Motion.RippleTrimStart" or "9to1.Motion.RippleTrimEnd" or "9to1.Motion.MoveToTrack" or "9to1.Motion.Split" or "9to1.Motion.Move" or "9to1.Motion.Slip" or "9to1.Motion.Trim" or "9to1.Motion.Delete" or "9to1.Motion.RippleDelete" or "9to1.Motion.Duplicate" => Element is not null && !Track.Locked,
            "9to1.Motion.Insert" or "9to1.Motion.Overwrite" => Project.AssetReferences.Count > 0 && !Track.Locked,
            "9to1.Motion.SaveCaption" or "9to1.Motion.SplitCaption" => Caption is not null,
            "9to1.Motion.MergeCaption" => Caption is not null && _captionIndex + 1 < Captions!.Cues.Count,
            "9to1.Motion.Reload" or "9to1.Motion.AddTrack" or "9to1.Motion.ToggleLock" or "9to1.Motion.ToggleVisibility" or "9to1.Motion.Extract" or "9to1.Motion.Lift"
                or "9to1.Motion.ImportSrt" or "9to1.Motion.ImportWebVtt" or "9to1.Motion.ExportSrt" or "9to1.Motion.ExportWebVtt"
                or "9to1.Motion.AddCaption" or "9to1.Motion.PreviousFrame" or "9to1.Motion.NextFrame" => true,
            _ => false
        };
    }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        => new(AdmitOriginalCommand(command, parameter, cancellationToken));
    private async Task DispatchCoreAsync(string command, object? parameter, CancellationToken token)
    {
        try
        {
            var sequence = Sequence; var selected = Element;
            var priorMarker = SelectedMarker;
            var priorCaption = Caption; var priorCaptionTrackId = Captions?.TrackId;
            long Number(string key) => long.Parse(_drafts[key], NumberStyles.None, CultureInfo.InvariantCulture);
            void Edit(Func<MotionProject, MotionProject> edit) { token.ThrowIfCancellationRequested(); using (EnterPhysicalOriginal()) _session.Apply(edit); }
            void CaptionsEdit(MotionCaptionTrack track, Guid? selectCueId = null)
            {
                Edit(p => _store.SetCaptions(p, p.Revision, _sequenceId, track));
                _captionTrackIndex = Sequence.CaptionTracks!.ToList().FindIndex(t => t.TrackId == track.TrackId);
                _captionIndex = Math.Max(0, track.Cues.ToList().FindIndex(c => c.CueId == (selectCueId ?? priorCaption?.CueId)));
            }
            if (!DispatchMarker(command, parameter, token)) switch (command)
            {
                case "9to1.Motion.DiscardDrafts": RestoreAcknowledgedDrafts(); break;
                case "9to1.Motion.Reload": using (EnterPhysicalOriginal()) _session.Reload(); break;
                case "9to1.Motion.Undo": using (EnterPhysicalOriginal()) _session.Undo(); break;
                case "9to1.Motion.Redo": using (EnterPhysicalOriginal()) _session.Redo(); break;
                case "9to1.Motion.AddTrack": Edit(p => _store.AddTrack(p, p.Revision, _sequenceId, $"Video {sequence.VideoTracks.Count + 1}")); break;
                case "9to1.Motion.ToggleLock": Edit(p => _store.SetTrackState(p, p.Revision, _sequenceId, _trackId, !Track.Locked, Track.Visible)); break;
                case "9to1.Motion.ToggleVisibility": Edit(p => _store.SetTrackState(p, p.Revision, _sequenceId, _trackId, Track.Locked, !Track.Visible)); break;
                case "9to1.Motion.Insert": Edit(p => _store.Insert(p, p.Revision, _sequenceId, _trackId, p.AssetReferences[_assetIndex].AssetId, Number("Start"), Number("SourceIn"), Number("SourceOut"))); break;
                case "9to1.Motion.Overwrite": Edit(p => _store.Overwrite(p, p.Revision, _sequenceId, _trackId, p.AssetReferences[_assetIndex].AssetId, Number("Start"), Number("SourceIn"), Number("SourceOut"))); break;
                case "9to1.Motion.MoveToTrack": Edit(p => _store.MoveToTrack(p, p.Revision, _sequenceId, selected!.ElementId, Sequence.VideoTracks[_destinationTrackIndex].TrackId, Number("Start"))); break;
                case "9to1.Motion.Slide": Edit(p => _store.Slide(p, p.Revision, _sequenceId, selected!.ElementId, Number("Start"))); break;
                case "9to1.Motion.Roll":
                    var next = Track.Elements.SingleOrDefault(e => e.ElementId != selected!.ElementId && e.TimelineStart == selected.TimelineStart + selected.Duration)
                        ?? throw new InvalidOperationException("Select a clip with a touching next clip to roll its cut.");
                    Edit(p => _store.Roll(p, p.Revision, _sequenceId, selected!.ElementId, next.ElementId, _playhead)); break;
                case "9to1.Motion.RippleTrimStart": Edit(p => _store.RippleTrim(p, p.Revision, _sequenceId, selected!.ElementId, MotionTrimEdge.Start, Number("SourceIn"))); break;
                case "9to1.Motion.RippleTrimEnd": Edit(p => _store.RippleTrim(p, p.Revision, _sequenceId, selected!.ElementId, MotionTrimEdge.End, Number("SourceOut"))); break;
                case "9to1.Motion.Split": Edit(p => _store.Split(p, p.Revision, _sequenceId, selected!.ElementId, _playhead)); break;
                case "9to1.Motion.Move": Edit(p => _store.Move(p, p.Revision, _sequenceId, selected!.ElementId, Number("Start"))); break;
                case "9to1.Motion.Slip": Edit(p => _store.Slip(p, p.Revision, _sequenceId, selected!.ElementId, Number("SourceIn"))); break;
                case "9to1.Motion.Trim": Edit(p => _store.Trim(p, p.Revision, _sequenceId, selected!.ElementId, Number("SourceIn"), Number("SourceOut"))); break;
                case "9to1.Motion.Duplicate": Edit(p => _store.Duplicate(p, p.Revision, _sequenceId, selected!.ElementId)); break;
                case "9to1.Motion.Delete": case "9to1.Motion.RippleDelete": Edit(p => _store.Delete(p, p.Revision, _sequenceId, selected!.ElementId, command.EndsWith("RippleDelete", StringComparison.Ordinal))); break;
                case "9to1.Motion.Extract": case "9to1.Motion.Lift": Edit(p => _store.RemoveRange(p, p.Revision, _sequenceId, Number("RangeStart"), Number("RangeEnd"), command.EndsWith("Extract", StringComparison.Ordinal))); break;
                case "9to1.Motion.ImportSrt": CaptionsEdit(MotionCaptions.ImportSrt(_drafts["SubtitleText"], "Captions", "und", sequence.FrameRateNumerator, sequence.FrameRateDenominator)); break;
                case "9to1.Motion.ImportWebVtt": CaptionsEdit(MotionCaptions.ImportWebVtt(_drafts["SubtitleText"], "Captions", "und", sequence.FrameRateNumerator, sequence.FrameRateDenominator)); break;
                case "9to1.Motion.ExportSrt": _drafts["SubtitleText"] = MotionCaptions.ExportSrt(Captions ?? new(Guid.NewGuid(), "Captions", "und", []), sequence.FrameRateNumerator, sequence.FrameRateDenominator); break;
                case "9to1.Motion.ExportWebVtt": _drafts["SubtitleText"] = MotionCaptions.ExportWebVtt(Captions ?? new(Guid.NewGuid(), "Captions", "und", []), sequence.FrameRateNumerator, sequence.FrameRateDenominator); break;
                case "9to1.Motion.AddCaption":
                    var captions = Captions ?? new(Guid.NewGuid(), "Captions", "und", []);
                    var newCue = new MotionCaptionCue(Guid.NewGuid(), Number("RangeStart"), Number("RangeEnd"), _drafts["CaptionText"]);
                    CaptionsEdit(captions with { Cues = captions.Cues.Append(newCue).ToArray() }, newCue.CueId); break;
                case "9to1.Motion.SaveCaption": CaptionsEdit(MotionCaptions.UpdateCue(Captions!, Caption!.CueId, Number("RangeStart"), Number("RangeEnd"), _drafts["CaptionText"])); break;
                case "9to1.Motion.SplitCaption": CaptionsEdit(MotionCaptions.SplitCue(Captions!, Caption!.CueId, _playhead, Caption.Text, _drafts["CaptionText"])); break;
                case "9to1.Motion.MergeCaption": CaptionsEdit(MotionCaptions.MergeCues(Captions!, Caption!.CueId, Captions!.Cues.ElementAt(_captionIndex + 1).CueId)); break;
                case "9to1.Motion.PreviousFrame": _playhead = Math.Max(0, _playhead - 1); break;
                case "9to1.Motion.NextFrame": _playhead = checked(_playhead + 1); break;
                case "9to1.Motion.ImportAsset": var asset = await SourceAsync(() => _pickAsset!(token)); token.ThrowIfCancellationRequested(); if (asset is null) { if (!_retiring) _status = "No media selected."; return; } Edit(p => _store.AddAsset(p, p.Revision, asset)); break;
                case "9to1.Motion.RelinkAsset":
                    var replacement = await SourceAsync(() => _pickAsset!(token)); token.ThrowIfCancellationRequested();
                    if (replacement is null) { if (!_retiring) _status = "No replacement media selected."; return; }
                    Edit(p => _store.RelinkAsset(p, p.Revision, p.AssetReferences[_assetIndex].AssetId, replacement.FileId, replacement.SourceRevisionID ?? throw new InvalidDataException("The selected source has no retained revision."))); break;
                case "9to1.Motion.PlaySource":
                    if (_playback is not null) await SourceAsync(_playback.CloseAndDrainAsync); _playback = null;
                    var openedPlayback = await SourceAsync(() => _media!.OpenSourceOriginalAsync(Project.AssetReferences.Single(a => a.AssetId == selected!.AssetId), token, OriginalSourceObserver));
                    _playback = openedPlayback; // Retain the actual acquired resource before cancellation/currentness checks.
                    token.ThrowIfCancellationRequested();
                    var seek = await SourceAsync(() => _playback.Session.SeekAsync(MotionProjectStore.Timebase(sequence).At(selected!.SourceIn + Math.Clamp(_playhead - selected.TimelineStart, 0, selected.Duration - 1)), token));
                    if (!seek.IsSuccess) throw new MotionMediaException(seek.Error!);
                    var play = await SourceAsync(() => _playback.Session.SetStateAsync(MediaPlaybackState.Playing, token)); if (!play.IsSuccess) throw new MotionMediaException(play.Error!); break;
                case "9to1.Motion.Pause": var pause = await SourceAsync(() => _playback!.Session.SetStateAsync(MediaPlaybackState.Paused, token)); if (!pause.IsSuccess) throw new MotionMediaException(pause.Error!); break;
                case "9to1.Motion.Render": var output = await SourceAsync(() => _exportTarget!(token)); if (output is null) { if (!_retiring) _status = "Export cancelled."; return; } await SourceAsync(() => _media!.RenderOriginalAsync(Project, _sequenceId, output, new MotionProgress(this), token, OriginalSourceObserver)); break;
                default: throw new NotSupportedException(command);
            }
            if (!Project.Sequences.Any(s => s.SequenceId == _sequenceId)) _sequenceId = Project.Sequences[0].SequenceId;
            if (_destinationTrackIndex >= Sequence.VideoTracks.Count) _destinationTrackIndex = 0;
            if (!Sequence.VideoTracks.Any(t => t.TrackId == _trackId)) _trackId = Sequence.VideoTracks[0].TrackId;
            if (_assetIndex >= Project.AssetReferences.Count) _assetIndex = 0;
            if (command is "9to1.Motion.Reload" or "9to1.Motion.Undo" or "9to1.Motion.Redo")
            {
                _captionTrackIndex = Math.Max(0, Sequence.CaptionTracks?.ToList().FindIndex(t => t.TrackId == priorCaptionTrackId) ?? 0);
                _captionIndex = Math.Max(0, Captions?.Cues.ToList().FindIndex(c => c.CueId == priorCaption?.CueId) ?? 0);
            }
            if (_captionTrackIndex >= (Sequence.CaptionTracks?.Count ?? 0)) _captionTrackIndex = 0;
            if (_captionIndex >= (Captions?.Cues.Count ?? 0)) _captionIndex = 0;
            if (priorCaption != Caption || command == "9to1.Motion.Reload") LoadCaption();
            if (SelectedMarker is null) _markerId = null;
            if (priorMarker != SelectedMarker || command == "9to1.Motion.Reload") LoadMarkerDrafts();
            AcknowledgeAcceptedDrafts(command);
            if (Element is null) _elementId = null;
            else
            {
                _trackId = Element.TrackId;
                if (selected != Element || command == "9to1.Motion.Reload")
                {
                    _drafts["Start"] = Element.TimelineStart.ToString(CultureInfo.InvariantCulture);
                    _drafts["SourceIn"] = Element.SourceIn.ToString(CultureInfo.InvariantCulture);
                    _drafts["SourceOut"] = Element.SourceOut.ToString(CultureInfo.InvariantCulture);
                    AcknowledgeDrafts("Start", "SourceIn", "SourceOut");
                }
            }
            if (!_retiring) _status = $"{command["9to1.Motion.".Length..]} complete · revision {Project.Revision}";
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or OverflowException or FormatException or KeyNotFoundException or NotSupportedException or OperationCanceledException)
        { if (!_retiring) _status = error is OperationCanceledException ? "Operation cancelled. Saved project edits and pending fields are preserved." : error.Message; throw; }
    }
    private void LoadCaption()
    {
        if (Caption is { } cue) { _drafts["CaptionText"] = cue.Text; _drafts["RangeStart"] = cue.StartFrame.ToString(CultureInfo.InvariantCulture); _drafts["RangeEnd"] = cue.EndFrame.ToString(CultureInfo.InvariantCulture); }
        else { _drafts["CaptionText"] = ""; _drafts["RangeStart"] = "0"; _drafts["RangeEnd"] = "30"; }
        AcknowledgeDrafts("CaptionText", "RangeStart", "RangeEnd");
    }
    private void Changed()
    {
        if (_retiring) return;
        using (EnterPhysicalOriginal()) PropertyChanged?.Invoke(this, new(string.Empty));
    }
    private sealed class MotionProgress(MotionCuiWorkspace owner) : IProgress<MediaRenderProgress>
    {
        public void Report(MediaRenderProgress value)
        {
            using (owner.EnterPhysicalOriginal())
            {
                if (owner._retiring) return;
                owner._status = $"Render {value.State} · {value.Fraction:P0}";
                owner.Changed();
            }
        }
    }
}
