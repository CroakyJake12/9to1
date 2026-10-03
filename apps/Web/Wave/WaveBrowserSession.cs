using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Wave;
using NineToOne.Web.Media;

namespace NineToOne.Web.Wave;

/// <summary>Device presentation/history over the real Wave model, serializer, edits and renderer.</summary>
public sealed class WaveBrowserSession : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IWaveBrowserMedia _media;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private WaveProject? _project;
    private long _savedRevision = -1;
    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private readonly Dictionary<string, WaveLocalSource> _sources = new(StringComparer.Ordinal);
    private WaveLocalProjectSummary[] _projects = [];
    private bool _disposed;
    private int _trackIndex;
    private int _clipIndex;
    private int _projectIndex;
    private string _title = "Untitled audio";
    private string _sampleRate = "48000";
    private string _channels = "2";
    private string _start = "0";
    private string _trimStart = "0";
    private string _trimEnd = "0";
    private string _gain = "1";
    private string _fadeIn = "0";
    private string _fadeOut = "0";
    private string _pan = "0";
    private string _seek = "0";
    private string _marker = "Marker";
    private string _joinClipId = "";
    private string _lastProjectId = "";
    private bool _loop;
    private double _position;
    private bool _paused = true;
    private Dictionary<string, object?>[] _waveform = [];
    private string _previewStatus = "Play or export to compute the mixed waveform.";
    private string? _presentationWarning;

    public WaveBrowserSession(IWaveBrowserMedia media) => _media = media;
    public event PropertyChangedEventHandler? PropertyChanged;
    public WaveProject? Project => _project;
    public bool IsDirty { get; private set; }
    public bool IsBusy { get; private set; }
    public string Status { get; private set; } = "Create a project or open one saved in this browser.";
    public string? ErrorCode { get; private set; }
    public long SavedRevision => _savedRevision;

    public async Task RefreshAsync(CancellationToken token = default)
    {
        var projects = (await CallAsync("list", new { }, token)).Deserialize<WaveLocalProjectSummary[]>(Json) ?? [];
        ThrowIfDisposed(); _projects = projects;
        Notify();
    }

    public Task<bool> CreateAsync(CancellationToken token = default) => OperateAsync(async () =>
    {
        if (!await SaveBeforeLeavingAsync(token)) return false;
        var project = WaveProjectStore.Create(_title, ParseInt(_sampleRate), ParseInt(_channels));
        await CallAsync("unload", new { }, token);
        ThrowIfDisposed();
        _project = project; _savedRevision = -1; _undo.Clear(); _redo.Clear(); _sources.Clear();
        _trackIndex = _clipIndex = 0; _waveform = []; SetDirty(true);
        return await SaveCoreAsync(token);
    }, token);

    public Task<bool> OpenAsync(string projectId, CancellationToken token = default) => OperateAsync(async () =>
    {
        if (!Guid.TryParse(projectId, out var id) || id == Guid.Empty) throw new ArgumentException("Choose a valid local project.");
        if (!await SaveBeforeLeavingAsync(token)) return false;
        var bundle = (await CallAsync("open", new { projectId }, token)).Deserialize<WaveLocalBundle>(Json)
            ?? throw new InvalidDataException("The stored project is invalid.");
        ThrowIfDisposed();
        if (bundle.ProjectId != projectId || bundle.Sources.Length > 256 || bundle.Undo.Length > 32 || bundle.Redo.Length > 32)
            throw new InvalidDataException("The stored project package is invalid.");
        var restoredSources = new Dictionary<string, WaveLocalSource>(StringComparer.Ordinal);
        foreach (var source in bundle.Sources)
        {
            RequireLocalSourcePath(id, source.Path);
            if (restoredSources.ContainsKey(source.Path)) throw new InvalidDataException("Duplicate source path.");
            var bytes = Convert.FromBase64String(source.Base64);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), source.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SourceChanged: stored source integrity check failed.");
            restoredSources.Add(source.Path, source);
        }
        var project = ReadProject(id, bundle.ProjectJson);
        if (project.ProjectId != id || project.Revision != bundle.Revision)
            throw new InvalidDataException("The stored project identity or revision is invalid.");
        ValidateSources(project, restoredSources);
        // History is actual owner serializer output. Reject corrupt/nonlocal snapshots before changing the view.
        foreach (var history in bundle.Undo.Concat(bundle.Redo)) ValidateSources(ReadProject(id, history), restoredSources);
        foreach (var source in restoredSources.Values)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(source.Path)!);
            File.WriteAllBytes(source.Path, Convert.FromBase64String(source.Base64));
        }
        await CallAsync("unload", new { }, token);
        ThrowIfDisposed();
        _project = project; _savedRevision = project.Revision; _lastProjectId = projectId;
        _title = bundle.Name; _sources.Clear(); foreach (var item in restoredSources) _sources.Add(item.Key, item.Value);
        _undo.Clear(); _undo.AddRange(bundle.Undo); _redo.Clear(); _redo.AddRange(bundle.Redo);
        _trackIndex = _clipIndex = 0; _paused = true; _position = 0; SetDirty(false); BuildWaveform();
        Status = $"Opened local project · revision {_savedRevision}";
        return true;
    }, token);

    public Task<bool> ImportAsync(CancellationToken token = default) => OperateAsync(async () =>
    {
        var project = RequireProject();
        var picked = (await CallAsync("pick", new { }, token)).Deserialize<WavePickedFile>(Json)
            ?? throw new InvalidDataException("The selected file could not be read.");
        ThrowIfDisposed();
        var bytes = Convert.FromBase64String(picked.Base64);
        var path = Path.Combine(ProjectDirectory(project.ProjectId), $"{Guid.NewGuid():N}.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
        try
        {
            var candidate = WaveProjectStore.AddWavClip(project, SelectedTrack().TrackId, path, Seconds(_start));
            _sources.Add(path, new(path, picked.Name, Convert.ToHexString(SHA256.HashData(bytes)), picked.Base64));
            Apply(candidate); _clipIndex = SelectedTrack().Clips.Count - 1;
            return await SaveCoreAsync(token);
        }
        catch (NotSupportedException error)
        {
            if (!_sources.ContainsKey(path)) File.Delete(path);
            throw new WaveBrowserException("CodecUnsupported", error.Message);
        }
        catch { if (!_sources.ContainsKey(path)) File.Delete(path); throw; }
    }, token);

    public Task<bool> SaveAsync(CancellationToken token = default) => OperateAsync(() => SaveCoreAsync(token), token);

    public Task<bool> CloseAsync(CancellationToken token = default) => OperateAsync(async () =>
    {
        if (!await SaveBeforeLeavingAsync(token)) return false;
        if (_project is not null) _lastProjectId = _project.ProjectId.ToString();
        await CallAsync("unload", new { }, token);
        ThrowIfDisposed();
        _project = null; _sources.Clear(); _undo.Clear(); _redo.Clear(); _savedRevision = -1; _waveform = [];
        _position = 0; _paused = true; SetDirty(false); Status = "Closed. Your saved project remains in this browser.";
        return true;
    }, token);

    public Task<bool> EditAsync(Func<WaveProject, WaveProject> edit, CancellationToken token = default) => OperateAsync(async () =>
    {
        Apply(edit(RequireProject()));
        return await SaveCoreAsync(token);
    }, token);

    private void Apply(WaveProject candidate)
    {
        var before = RequireProject();
        if (candidate.ProjectId != before.ProjectId || candidate.Revision != checked(before.Revision + 1))
            throw new InvalidDataException("The owner edit returned an incompatible project revision.");
        WaveProjectStore.Validate(candidate);
        _undo.Add(Serialize(before)); if (_undo.Count > 32) _undo.RemoveAt(0);
        _redo.Clear(); _project = candidate; SetDirty(true); _waveform = [];
    }

    public Task<bool> UndoAsync(bool redo = false, CancellationToken token = default) => OperateAsync(async () =>
    {
        var current = RequireProject(); var from = redo ? _redo : _undo; var to = redo ? _undo : _redo;
        if (from.Count == 0) throw new WaveBrowserException("HistoryUnavailable", redo ? "There is no edit to redo." : "There is no edit to undo.");
        var restored = ReadProject(current.ProjectId, from[^1]);
        // Same owner revision/ModifiedAt rule as WaveProjectEdits.Commit; history never rewinds revision.
        restored = restored with { Revision = checked(current.Revision + 1), ModifiedAt = DateTimeOffset.UtcNow };
        WaveProjectStore.Validate(restored); ValidateSources(restored, _sources);
        to.Add(Serialize(current)); if (to.Count > 32) to.RemoveAt(0); from.RemoveAt(from.Count - 1);
        _project = restored; _trackIndex = Math.Clamp(_trackIndex, 0, Math.Max(0, restored.Tracks.Count - 1)); _clipIndex = 0;
        SetDirty(true); _waveform = [];
        return await SaveCoreAsync(token);
    }, token);

    private async Task<bool> SaveBeforeLeavingAsync(CancellationToken token) => !IsDirty || await SaveCoreAsync(token);

    private async Task<bool> SaveCoreAsync(CancellationToken token)
    {
        if (_project is null) throw new WaveBrowserException("ProjectNotFound", "Open a project first.");
        if (!IsDirty) { Status = $"Saved in this browser · revision {_savedRevision}"; return true; }
        var project = _project;
        ValidateSources(project, _sources);
        var bundle = new WaveLocalBundle(project.ProjectId.ToString(), _title, project.Revision,
            Serialize(project), _sources.Values.ToArray(), _undo.ToArray(), _redo.ToArray());
        token.ThrowIfCancellationRequested();
        var result = await CallAsync("commit", new { bundle, expectedRevision = _savedRevision }, token);
        if (result.GetProperty("revision").GetInt64() != project.Revision)
            throw new InvalidDataException("The local save acknowledgement has an incompatible revision.");
        // The transaction committed even if this view was disposed while awaiting its receipt.
        // Do not restore detached presentation or touch a subsequent view's dirty indicator.
        if (_disposed) return true;
        _savedRevision = project.Revision; _lastProjectId = project.ProjectId.ToString(); SetDirty(false);
        Status = $"Saved in this browser · revision {_savedRevision}";
        try { await RefreshAsync(); }
        catch (Exception error)
        {
            Console.Error.WriteLine($"WavePostCommitListFailed: {error.GetType().Name}");
            if (!_disposed) Status += "; the project list could not refresh. Refresh the list to retry; the edit was saved.";
        }
        return true;
    }

    public Task<bool> ExportAsync(bool playback = false, CancellationToken token = default) => OperateAsync(async () =>
    {
        var project = RequireProject();
        CheckRenderCapacity(project);
        var path = Path.Combine(ProjectDirectory(project.ProjectId), $"export-{Guid.NewGuid():N}.wav");
        try
        {
            WaveProjectExporter.ExportPcm16(project, path, token);
            var data = File.ReadAllBytes(path);
            SetWaveform(data, project.Channels);
            await CallAsync(playback ? "play" : "download", new {
                base64 = Convert.ToBase64String(data), name = "Wave-" + project.ProjectId.ToString("N") + ".wav", mime = "audio/wav", loop = _loop
            }, token);
            Status = playback ? $"Playing local project revision {project.Revision}" : $"WAV download started for revision {project.Revision}. Project and sources are unchanged.";
            return true;
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }, token);

    public async Task PollTransportAsync()
    {
        if (_disposed || IsBusy || _project is null) return;
        try
        {
            var state = await CallAsync("state", new { });
            if (_disposed) return;
            _position = state.GetProperty("position").GetDouble(); _paused = state.GetProperty("paused").GetBoolean();
            Notify("Transport");
        }
        catch (WaveBrowserException) { /* Polling never overwrites an actionable edit/save error. */ }
    }

    public bool TryGetValue(string path, out object? value)
    {
        if (_disposed) { value = null; return false; }
        var track = _project?.Tracks.ElementAtOrDefault(_trackIndex);
        var clip = track?.Clips.ElementAtOrDefault(_clipIndex);
        value = path switch
        {
            "Title" => _title, "SampleRate" => _sampleRate, "Channels" => _channels,
            "Start" => _start, "TrimStart" => _trimStart, "TrimEnd" => _trimEnd,
            "Gain" => _gain, "FadeIn" => _fadeIn, "FadeOut" => _fadeOut, "Pan" => _pan,
            "Seek" => _seek, "MarkerName" => _marker, "Loop" => _loop, "JoinClipId" => _joinClipId,
            "LoopLabel" => _loop ? "Loop playback: on" : "Loop playback: off",
            "ProjectIndex" => _projectIndex, "TrackIndex" => _trackIndex, "ClipIndex" => _clipIndex,
            "ProjectChoices" => _projects.Select(p => $"{p.Name} · r{p.Revision} · {p.ProjectId}").ToArray(),
            "Projects" => _projects.Select(p => new Dictionary<string, object?> { ["ID"] = p.ProjectId, ["Label"] = $"Open {p.Name} · revision {p.Revision} · {p.ProjectId}" }).ToArray(),
            "Tracks" => _project?.Tracks.Select(t => new Dictionary<string, object?> { ["ID"] = t.TrackId.ToString(), ["Label"] = $"{(t.TrackId == track?.TrackId ? "Selected: " : "Select ")}{t.Name} · {t.TrackId}" }).ToArray() ?? [],
            "Clips" => track?.Clips.Select(c => new Dictionary<string, object?> { ["ID"] = c.ClipId.ToString(), ["Label"] = $"{(c.ClipId == clip?.ClipId ? "Selected clip: " : "Select clip ")}{c.ClipId} · {c.FrameCount} frames" }).ToArray() ?? [],
            "TrackChoices" => _project?.Tracks.Select(t => $"{t.Name} · {t.TrackId}").ToArray() ?? [],
            "ClipChoices" => track?.Clips.Select(c => $"{Path.GetFileName(c.SourcePath)} · {c.ClipId}").ToArray() ?? [],
            "HasProject" => _project is not null, "HasClip" => clip is not null,
            "CanEdit" => _project is not null && !IsBusy, "NotBusy" => !IsBusy,
            "CanUndo" => _undo.Count > 0 && !IsBusy, "CanRedo" => _redo.Count > 0 && !IsBusy,
            "Status" => (ErrorCode is null ? "" : ErrorCode + ": ") + Status + (_presentationWarning is null ? "" : " · " + _presentationWarning),
            "ProjectIdentity" => _project is null ? "No project open" : $"Project {_project.ProjectId} · {_project.SampleRate} Hz · {_project.Channels} channels · revision {_project.Revision}",
            "Selection" => clip is null ? "Choose an imported clip." : $"Clip {clip.ClipId} · source {clip.SourceReferenceId} · source frames {clip.SourceStartFrame}–{clip.SourceStartFrame + clip.FrameCount} · timeline frame {clip.TimelineStartFrame} · gain {clip.Gain} · fades {clip.FadeInFrames}/{clip.FadeOutFrames} frames",
            "Mixer" => track is null ? "" : $"Track {track.TrackId} · gain {track.Gain} · pan {track.Pan} · mute {track.Mute} · solo {track.Solo}",
            "Transport" => $"{(_paused ? "Paused/stopped" : "Playing")} · {_position:0.000}s · {(_loop ? "Loop on" : "Loop off")}",
            "Waveform" => _waveform,
            "PreviewStatus" => _previewStatus,
            "Timeline" => TimelineRows(),
            "Annotations" => _project is null ? "" : string.Join("; ", _project.Markers.Select(m => $"{m.Name} at frame {m.Frame} ({m.MarkerId})")),
            _ => null
        };
        return value is not null;
    }

    public bool TrySetValue(string path, object? value)
    {
        if (_disposed || IsBusy) return false;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        switch (path)
        {
            case "Title": _title = text; break;
            case "SampleRate": _sampleRate = text; break; case "Channels": _channels = text; break;
            case "Start": _start = text; break; case "TrimStart": _trimStart = text; break; case "TrimEnd": _trimEnd = text; break;
            case "Gain": _gain = text; break; case "FadeIn": _fadeIn = text; break; case "FadeOut": _fadeOut = text; break;
            case "Pan": _pan = text; break; case "Seek": _seek = text; break; case "MarkerName": _marker = text; break;
            case "JoinClipId": _joinClipId = text; break;
            case "TrackIndex": _trackIndex = Math.Max(0, Convert.ToInt32(value)); _clipIndex = 0; break;
            case "ClipIndex": _clipIndex = Math.Max(0, Convert.ToInt32(value)); break; case "ProjectIndex": _projectIndex = Math.Max(0, Convert.ToInt32(value)); break;
            case "Loop": _loop = Convert.ToBoolean(value); break;
            default: return false;
        }
        Notify(); return true;
    }

    public bool? IsActionAvailable(string command) => new[] { "Create", "Refresh", "Open", "Reopen", "Import", "Save", "Close",
        "AddTrack", "Split", "Trim", "Move", "Duplicate", "Delete", "RippleDelete", "Processing", "Mixer", "Mute", "Solo",
        "Undo", "Redo", "Export", "Play", "Pause", "Resume", "Stop", "Seek", "Loop", "Marker",
        "OpenProject", "OpenProjectRow", "SelectTrack", "SelectTrackRow", "SelectClip", "SelectClipRow", "Join", "ToggleLoop" }.Contains(command);

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case "OpenProject": await OpenAsync(Convert.ToString(parameter) ?? "", cancellationToken); return;
            case "SelectTrack": case "SelectClip":
                await OperateAsync(() =>
                {
                    var id = Guid.Parse(Convert.ToString(parameter) ?? "");
                    if (command == "SelectTrack")
                    {
                        var index = RequireProject().Tracks.FindIndex(t => t.TrackId == id);
                        if (index < 0) throw new KeyNotFoundException("TrackNotFound");
                        _trackIndex = index; _clipIndex = 0;
                    }
                    else
                    {
                        var index = SelectedTrack().Clips.FindIndex(c => c.ClipId == id);
                        if (index < 0) throw new KeyNotFoundException("ClipNotFound");
                        _clipIndex = index;
                    }
                    Status = "Selection updated."; return Task.FromResult(true);
                }, cancellationToken); return;
            case "Create": await CreateAsync(cancellationToken); return;
            case "Open": await OpenAsync(_projects.ElementAtOrDefault(_projectIndex)?.ProjectId ?? "", cancellationToken); return;
            case "Reopen": await OpenAsync(_lastProjectId, cancellationToken); return;
            case "Import": await ImportAsync(cancellationToken); return;
            case "Save": await SaveAsync(cancellationToken); return;
            case "Close": await CloseAsync(cancellationToken); return;
            case "Undo": await UndoAsync(false, cancellationToken); return; case "Redo": await UndoAsync(true, cancellationToken); return;
            case "Export": await ExportAsync(false, cancellationToken); return; case "Play": await ExportAsync(true, cancellationToken); return;
            case "Refresh": await OperateAsync(async () => { await RefreshAsync(cancellationToken); Status = "Local projects refreshed."; return true; }, cancellationToken); return;
            case "ToggleLoop":
                await OperateAsync(async () => { _loop = !_loop; await CallAsync("loop", new { loop = _loop }, cancellationToken); Status = _loop ? "Loop playback enabled." : "Loop playback disabled."; return true; }, cancellationToken); return;
            case "Pause": case "Resume": case "Stop": case "Seek": case "Loop":
                await OperateAsync(async () => { await CallAsync(command.ToLowerInvariant(), new { seconds = Seconds(_seek), loop = _loop }, cancellationToken); Status = "Transport updated."; return true; }, cancellationToken); return;
        }
        await EditAsync(project => command switch
        {
            "AddTrack" => WaveProjectEdits.AddTrack(project, project.Revision, _title),
            "Split" => WaveProjectEdits.Split(project, project.Revision, SelectedClip().ClipId, Frames(_start)),
            "Join" => WaveProjectEdits.Join(project, project.Revision, SelectedClip().ClipId, Guid.Parse(_joinClipId)),
            "Trim" => WaveProjectEdits.Trim(project, project.Revision, SelectedClip().ClipId, Frames(_trimStart), Frames(_trimEnd)),
            "Move" => WaveProjectEdits.Move(project, project.Revision, SelectedClip().ClipId, Frames(_start)),
            "Duplicate" => WaveProjectEdits.Duplicate(project, project.Revision, SelectedClip().ClipId, Frames(_start)),
            "Delete" => WaveProjectEdits.Delete(project, project.Revision, SelectedClip().ClipId, false),
            "RippleDelete" => WaveProjectEdits.Delete(project, project.Revision, SelectedClip().ClipId, true),
            "Processing" => WaveProjectEdits.SetClipProcessing(project, project.Revision, SelectedClip().ClipId, Number(_gain), Frames(_fadeIn), Frames(_fadeOut)),
            "Mixer" => WaveProjectEdits.SetTrackMixer(project, project.Revision, SelectedTrack().TrackId, Number(_gain), Number(_pan), SelectedTrack().Mute, SelectedTrack().Solo),
            "Mute" => WaveProjectEdits.SetTrackMixer(project, project.Revision, SelectedTrack().TrackId, SelectedTrack().Gain, SelectedTrack().Pan, !SelectedTrack().Mute, SelectedTrack().Solo),
            "Solo" => WaveProjectEdits.SetTrackMixer(project, project.Revision, SelectedTrack().TrackId, SelectedTrack().Gain, SelectedTrack().Pan, SelectedTrack().Mute, !SelectedTrack().Solo),
            "Marker" => WaveTimelineAnnotations.AddMarker(project, project.Revision, Frames(_start), _marker),
            _ => throw new WaveBrowserException("CapabilityUnavailable", "This Wave operation is unavailable.")
        }, cancellationToken);
    }

    private async Task<bool> OperateAsync(Func<Task<bool>> operation, CancellationToken token)
    {
        if (!await _operations.WaitAsync(0, token)) return false;
        try
        {
            if (_disposed) return false;
            IsBusy = true; ErrorCode = null; Notify();
            return await operation();
        }
        catch (OperationCanceledException) { Status = "Operation cancelled. Prior saved state is intact."; ErrorCode = "OperationCancelled"; return false; }
        catch (WaveBrowserException error) { ErrorCode = error.Code; Status = error.Message; return false; }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException
            or OverflowException or FormatException or JsonException or NotSupportedException or KeyNotFoundException or UnauthorizedAccessException)
        {
            ErrorCode = error switch { UnauthorizedAccessException => "PermissionDenied", NotSupportedException => "CapabilityUnavailable",
                KeyNotFoundException => error.Message, FileNotFoundException => "SourceUnavailable", InvalidOperationException when error.Message == "RevisionConflict" => "RevisionConflict",
                InvalidDataException when error.Message.StartsWith("SourceChanged") => "SourceChanged", IOException => "StorageFailed", _ => "InvalidArgument" };
            Status = "The operation failed. Your prior saved project and original sources are intact. " + error.Message;
            return false;
        }
        finally { IsBusy = false; try { Notify(); } finally { _operations.Release(); } }
    }

    private async Task<JsonElement> CallAsync(string action, object arguments, CancellationToken token = default)
    {
        using var result = JsonDocument.Parse(await _media.InvokeAsync(action, JsonSerializer.Serialize(arguments, Json), token));
        if (!result.RootElement.GetProperty("ok").GetBoolean())
            throw new WaveBrowserException(result.RootElement.GetProperty("code").GetString()!, result.RootElement.GetProperty("message").GetString()!);
        return result.RootElement.GetProperty("value").Clone();
    }

    private static string ProjectDirectory(Guid id) => Path.Combine(Path.GetTempPath(), "9to1-wave-local", id.ToString("N"));
    private static string Serialize(WaveProject project)
    {
        var path = Path.Combine(ProjectDirectory(project.ProjectId), $"snapshot-{Guid.NewGuid():N}.json");
        try { WaveProjectStore.Save(path, project); return File.ReadAllText(path); }
        finally { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".lock")) File.Delete(path + ".lock"); }
    }
    private static WaveProject ReadProject(Guid id, string json)
    {
        Directory.CreateDirectory(ProjectDirectory(id));
        var path = Path.Combine(ProjectDirectory(id), $"read-{Guid.NewGuid():N}.json");
        try { File.WriteAllText(path, json); var project = WaveProjectStore.Open(path);
            if (project.ProjectId != id) throw new InvalidDataException("History identity differs from the project."); return project; }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private static void RequireLocalSourcePath(Guid id, string path)
    {
        var full = Path.GetFullPath(path);
        if (!string.Equals(full, path, StringComparison.Ordinal) || Path.GetDirectoryName(full) != ProjectDirectory(id)
            || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(full), "N", out _) || Path.GetExtension(full) != ".wav")
            throw new InvalidDataException("This local package contains an unavailable source path.");
    }
    private static void ValidateSources(WaveProject project, IReadOnlyDictionary<string, WaveLocalSource> sources)
    {
        WaveProjectStore.Validate(project);
        foreach (var clip in project.Tracks.SelectMany(track => track.Clips))
        {
            RequireLocalSourcePath(project.ProjectId, clip.SourcePath);
            if (clip.SourceFileID is not null || !sources.TryGetValue(clip.SourcePath, out var source)
                || !string.Equals(source.Sha256, clip.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SourceChanged: this local package does not contain the referenced source revision.");
        }
    }
    private void BuildWaveform()
    {
        if (_project is null || !_project.Tracks.Any(t => t.Clips.Count > 0)) { _waveform = []; return; }
        try { CheckRenderCapacity(_project); }
        catch (WaveBrowserException error) { _waveform = []; _previewStatus = error.Message; return; }
        var path = Path.Combine(ProjectDirectory(_project.ProjectId), $"preview-{Guid.NewGuid():N}.wav");
        try { WaveProjectExporter.ExportPcm16(_project, path); SetWaveform(File.ReadAllBytes(path), _project.Channels); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private void SetWaveform(byte[] rendered, int channels)
    {
        // The owner's PCM16 exporter emits this fixed RIFF header; this is a derived display, never source decoding.
        var frames = (rendered.Length - 44) / (channels * 2); var rows = new List<Dictionary<string, object?>>();
        for (var bucket = 0; bucket < 192 && frames > 0; bucket++)
        {
            var start = frames * (long)bucket / 192; var end = Math.Max(start + 1, frames * (long)(bucket + 1) / 192); double peak = 0;
            for (var frame = start; frame < Math.Min(end, frames); frame++)
                for (var channel = 0; channel < channels; channel++) peak = Math.Max(peak, Math.Abs((double)BitConverter.ToInt16(rendered, checked(44 + (int)(frame * channels + channel) * 2))) / 32768);
            rows.Add(new() { ["ID"] = bucket, ["Height"] = Math.Max(1, peak * 100), ["Description"] = $"Bucket {bucket + 1}: peak {peak:0.000}" });
        }
        _waveform = rows.ToArray();
        _previewStatus = $"192 peak buckets over {frames / (double)RequireProject().SampleRate:0.000}s of the canonical mix.";
    }
    private Dictionary<string, object?>[] TimelineRows() => _project?.Tracks.SelectMany(track => track.Clips.Select(clip => new Dictionary<string, object?>
    { ["ID"] = clip.ClipId.ToString(), ["Text"] = $"{track.Name} · {clip.TimelineStartFrame / (double)_project.SampleRate:0.000}s → {(clip.TimelineStartFrame + clip.FrameCount) / (double)_project.SampleRate:0.000}s · {clip.ClipId}" })).ToArray() ?? [];
    private WaveProject RequireProject() => _project ?? throw new WaveBrowserException("ProjectNotFound", "Create or open a local project first.");
    private static void CheckRenderCapacity(WaveProject project)
    {
        var frames = project.Tracks.SelectMany(track => track.Clips).Select(clip => checked(clip.TimelineStartFrame + clip.FrameCount)).DefaultIfEmpty(0).Max();
        if (frames > (16L * 1024 * 1024 - 44) / (project.Channels * 2))
            throw new WaveBrowserException("CapacityExceeded", "This foreground browser adapter renders up to 16 MiB of PCM16 audio. Your saved project is intact.");
    }
    private WaveTrack SelectedTrack() => RequireProject().Tracks.ElementAtOrDefault(_trackIndex) ?? throw new KeyNotFoundException("TrackNotFound");
    private WaveClip SelectedClip() => SelectedTrack().Clips.ElementAtOrDefault(_clipIndex) ?? throw new KeyNotFoundException("ClipNotFound");
    private static int ParseInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);
    private void ThrowIfDisposed() { if (_disposed) throw new OperationCanceledException("The local Wave view was disposed."); }
    private static double Number(string value) { var number = double.Parse(value, CultureInfo.InvariantCulture); return double.IsFinite(number) ? number : throw new ArgumentException("Use a finite number."); }
    private static double Seconds(string value) { var number = Number(value); return number >= 0 ? number : throw new ArgumentException("Use a non-negative time."); }
    private long Frames(string value) => checked((long)Math.Round(Seconds(value) * RequireProject().SampleRate, MidpointRounding.AwayFromZero));
    private void SetDirty(bool value)
    {
        IsDirty = value;
        try { _media.SetDirty(value); }
        catch (Exception error) { PresentationFailed(error, "The browser leave-page warning could not update."); }
    }
    private void PresentationFailed(Exception error, string message)
    {
        _presentationWarning = message;
        Console.Error.WriteLine($"WavePresentationFailed: {error.GetType().Name}");
    }
    private void Notify(string? propertyName = null)
    {
        foreach (var observer in PropertyChanged?.GetInvocationList().Cast<PropertyChangedEventHandler>() ?? [])
            try { observer(this, new(propertyName)); }
            catch (Exception error) { PresentationFailed(error, "A display update failed. Saved edits remain durable; reopen to recover the view."); }
    }
    public void SuspendPlayback()
    {
        try { _media.Release(); }
        catch (Exception error) { PresentationFailed(error, "Playback cleanup failed. Close the browser tab if audio continues."); throw; }
        finally { if (!_disposed) SetDirty(IsDirty); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Clear retained content before invoking external browser cleanup, which may fail.
        _project = null; _sources.Clear(); _undo.Clear(); _redo.Clear(); _waveform = []; _projects = [];
        _savedRevision = -1; IsDirty = false; _lastProjectId = ""; _title = "";
        _media.Release();
    }
}

// These are browser storage envelopes around opaque canonical serializer output, not alternate Wave entities.
public sealed record WaveLocalSource(string Path, string Name, string Sha256, string Base64);
public sealed record WaveLocalBundle(string ProjectId, string Name, long Revision, string ProjectJson,
    WaveLocalSource[] Sources, string[] Undo, string[] Redo);
public sealed record WaveLocalProjectSummary(string ProjectId, string Name, long Revision);
public sealed record WavePickedFile(string Name, string Base64);
