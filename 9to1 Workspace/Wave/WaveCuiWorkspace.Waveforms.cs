using Avalonia.Threading;
using Haven.Infrastructure.Media;

namespace HavenOS.Apps.Wave;

public sealed partial class WaveCuiWorkspace
{
    private sealed class OriginalWaveform(WaveCuiWorkspace owner, WaveProject project, WaveClip clip)
    {
        public WaveCuiWorkspace Owner { get; } = owner;
        public volatile bool Active;
        public WaveProject Project { get; } = project;
        public WaveClip Clip { get; } = clip;
        public Task? Driver { get; set; }
        public PcmWaveformRange? Data { get; set; }
        public string Status { get; set; } = "Analysing original source…";
    }
    private sealed class WaveformInvocation(OriginalWaveform source, WaveformInvocation? parent)
    { public OriginalWaveform Source { get; } = source; public WaveformInvocation? Parent { get; } = parent; public volatile bool Active = true; }
    private static readonly AsyncLocal<WaveformInvocation?> LogicalWaveform = new();
    [ThreadStatic] private static WaveformInvocation? PhysicalWaveform;
    private static bool SameOriginalWaveformSource(WaveClip original, WaveClip current) =>
        original.ClipId == current.ClipId && original.SourceReferenceId == current.SourceReferenceId &&
        original.SourceFileID == current.SourceFileID && original.SourceRevisionID == current.SourceRevisionID &&
        original.SourceSha256 == current.SourceSha256 && original.SourceStartFrame == current.SourceStartFrame &&
        original.FrameCount == current.FrameCount && original.AudioDerivation == current.AudioDerivation;
    private bool MatchesCurrentWaveform(OriginalWaveform source, WaveClip clip) =>
        source.Project.ProjectId == _snapshot.ProjectId && source.Project.SampleRate == _snapshot.SampleRate &&
        source.Project.Channels == _snapshot.Channels && SameOriginalWaveformSource(source.Clip, clip);
    private readonly Dictionary<Guid, OriginalWaveform> _waveforms = [];
    private readonly List<Task> _originalWaveformSources = [];
    private readonly object _originalWaveformGate = new();
    private bool _synchronizingWaveforms;
    public IReadOnlyList<Task> OriginalWaveformSources { get { lock (_originalWaveformGate) return _originalWaveformSources.ToArray(); } }
    private void RetainOriginalWaveform(Task original) { lock (_originalWaveformGate) _originalWaveformSources.Add(original); }
    public PcmWaveformRange? ObserveOriginalWaveform(WaveClip clip) =>
        _waveforms.TryGetValue(clip.ClipId, out var original) && MatchesCurrentWaveform(original, clip) ? original.Data : null;
    public string ObserveOriginalWaveformStatus(WaveClip clip) =>
        _waveforms.TryGetValue(clip.ClipId, out var original) && MatchesCurrentWaveform(original, clip)
            ? original.Status : "Connect authorised Files audio sources to show waveforms.";
    internal void SynchronizeOriginalWaveforms()
    {
        if (_synchronizingWaveforms || _retiring || _disposed || _files is null || !_available("9to1.Wave.AnalyzeWaveform")) return;
        _synchronizingWaveforms = true;
        try
        {
            lock (_originalWaveformGate) _originalWaveformSources.RemoveAll(source => source.IsCompletedSuccessfully);
            var clips = _snapshot.Tracks.SelectMany(track => track.Clips).ToArray();
            foreach (var old in _waveforms.Where(pair => !clips.Any(clip => clip.ClipId == pair.Key && MatchesCurrentWaveform(pair.Value, clip))).ToArray())
                _waveforms.Remove(old.Key);
            foreach (var clip in clips)
            {
                if (_waveforms.ContainsKey(clip.ClipId)) continue;
                if (OriginalWaveformSources.Count >= 64 || _waveforms.Count >= 64) break;
                var source = new OriginalWaveform(this, _snapshot, clip); _waveforms.Add(clip.ClipId, source);
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                source.Driver = AnalyzeOriginalWaveformAsync(start.Task, source); RetainOriginalWaveform(source.Driver); start.SetResult();
            }
        }
        finally { _synchronizingWaveforms = false; }
    }
    private async Task AnalyzeOriginalWaveformAsync(Task start, OriginalWaveform source)
    {
        var previous = LogicalWaveform.Value; var invocation = new WaveformInvocation(source, previous);
        LogicalWaveform.Value = invocation; source.Active = true;
        try
        {
        await start;
        var original = WithinOriginalWaveform(source, () => CaptureExternal(() => _files!.AnalyzeOriginalClipWaveformAsync(source.Project,
            source.Project.Revision, source.Clip.ClipId)));
        var result = await original;
        var publication = Dispatcher.UIThread.InvokeAsync(() =>
        {
            var previousPhysical = PhysicalWaveform; var physicalInvocation = new WaveformInvocation(source, previousPhysical); PhysicalWaveform = physicalInvocation;
            try
            {
            if (_retiring || _disposed || !_waveforms.TryGetValue(source.Clip.ClipId, out var current) || !ReferenceEquals(source, current) ||
                !_snapshot.Tracks.SelectMany(track => track.Clips).Any(clip => MatchesCurrentWaveform(source, clip))) return;
            if (result.IsSuccess) { source.Data = result.Value!; source.Status = "Original source waveform · " + source.Data.Channels.Count + " channels"; }
            else source.Status = "Waveform unavailable: " + result.Error!.Message;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(null));
            }
            finally { physicalInvocation.Active = false; PhysicalWaveform = previousPhysical; }
        }).GetTask();
        RetainOriginalWaveform(publication); await publication;
        }
        catch (Exception failure)
        {
            var failurePublication = Dispatcher.UIThread.InvokeAsync(() =>
            {
                WithinOriginalWaveform(source, () =>
                {
                    if (!_retiring && !_disposed && _waveforms.TryGetValue(source.Clip.ClipId, out var current) && ReferenceEquals(current, source))
                    {
                        source.Status = "Waveform could not finish: " + failure.Message;
                        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(null));
                    }
                    return true;
                });
            }).GetTask();
            RetainOriginalWaveform(failurePublication);
            try { await failurePublication; }
            catch (Exception publicationFailure) { throw new AggregateException("Wave retains analysis and error-publication failures.", failure, publicationFailure); }
            throw;
        }
        finally { invocation.Active = false; source.Active = false; LogicalWaveform.Value = previous; }
    }
    private static T WithinOriginalWaveform<T>(OriginalWaveform source, Func<T> callback)
    {
        var previous = PhysicalWaveform; var invocation = new WaveformInvocation(source, previous); PhysicalWaveform = invocation;
        try { return callback(); } finally { invocation.Active = false; PhysicalWaveform = previous; }
    }
    internal void DemandExternalWaveformJoin()
    {
        for (var logical = LogicalWaveform.Value; logical is not null; logical = logical.Parent)
            if (logical.Active && logical.Source.Active && ReferenceEquals(logical.Source.Owner, this))
                throw new InvalidOperationException("The original Wave waveform callback cannot join its own retirement.");
        for (var physical = PhysicalWaveform; physical is not null; physical = physical.Parent)
            if (physical.Active && physical.Source.Active && ReferenceEquals(physical.Source.Owner, this))
                throw new InvalidOperationException("The physical original Wave waveform callback cannot join its own retirement.");
    }
}
