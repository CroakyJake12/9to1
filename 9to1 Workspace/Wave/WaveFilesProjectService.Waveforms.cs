using Haven.Core.Media;
using Haven.Infrastructure.Media;

namespace HavenOS.Apps.Wave;

public sealed partial class WaveFilesProjectService
{
    // Failed drivers keep their SAME lease and raw sources. No raw failure is
    // converted to a successful unavailable result or retried behind the caller.
    private readonly List<OriginalWaveformAnalysis> _originalWaveformAnalyses = [];
    private readonly SemaphoreSlim _waveformSlots = new(2, 2);
    private readonly object _waveformGate = new();
    public Task<MediaEngineResult<PcmWaveformRange>> AnalyzeOriginalClipWaveformAsync(
        WaveProject project, long expectedRevision, Guid clipId, int buckets = 512, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        lock (_waveformGate)
        {
        _originalWaveformAnalyses.RemoveAll(owner => owner.Original.IsCompletedSuccessfully);
        if (_originalWaveformAnalyses.Count >= 64)
            throw new InvalidOperationException("Wave retains unresolved source analyses. Recover this same source owner before further analysis.");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new OriginalWaveformAnalysis(this, project, expectedRevision, clipId, buckets, start.Task, token);
        _originalWaveformAnalyses.Add(owner); start.SetResult(); return owner.Original;
        }
    }
    private sealed class OriginalWaveformAnalysis
    {
        private readonly WaveFilesProjectService _owner;
        private readonly List<Task> _raw = [];
        private MediaAssetReadLease? _source;
        private MediaAudioDecodedLease? _decoded;
        public Task<MediaEngineResult<PcmWaveformRange>> Original { get; }
        public OriginalWaveformAnalysis(WaveFilesProjectService owner, WaveProject project, long expectedRevision,
            Guid clipId, int buckets, Task start, CancellationToken token)
        { _owner = owner; Original = RunAsync(start, project, expectedRevision, clipId, buckets, token); }
        private Task<T> Retain<T>(Task<T> original) { _raw.Add(original); return original; }
        private async Task<MediaEngineResult<PcmWaveformRange>> RunAsync(Task start, WaveProject originalProject,
            long expectedRevision, Guid clipId, int buckets, CancellationToken token)
        {
            await start;
            var originalAdmission = _owner._waveformSlots.WaitAsync(token); _raw.Add(originalAdmission); await originalAdmission;
            try { return await AnalyzeWithinSlotAsync(originalProject, expectedRevision, clipId, buckets, token); }
            finally { _owner._waveformSlots.Release(); }
        }
        private async Task<MediaEngineResult<PcmWaveformRange>> AnalyzeWithinSlotAsync(WaveProject originalProject,
            long expectedRevision, Guid clipId, int buckets, CancellationToken token)
        {
            WaveProjectStore.Validate(originalProject);
            if (originalProject.Revision != expectedRevision) return Failure<PcmWaveformRange>(MediaEngineErrorCode.RevisionConflict);
            var project = originalProject with
            { Tracks = originalProject.Tracks.Select(track => track with { Clips = track.Clips.ToList() }).ToList(),
                Markers = originalProject.Markers.ToList(), Regions = originalProject.Regions.ToList() };
            var selected = project.Tracks.SelectMany(track => track.Clips).Where(clip => clip.ClipId == clipId).ToArray();
            if (selected.Length != 1) return Failure<PcmWaveformRange>(MediaEngineErrorCode.RevisionConflict);
            var clip = selected[0];
            if (string.IsNullOrWhiteSpace(clip.SourceFileID) || string.IsNullOrWhiteSpace(clip.SourceRevisionID))
                return Failure<PcmWaveformRange>(MediaEngineErrorCode.SourceUnavailable);
            MediaEngineResult<PcmWaveformRange>? result = null; Exception? bodyFailure = null;
            try
            {
                var resolved = await Retain(_owner._sources.ResolveAsync(clip.SourceFileID, new(clip.SourceReferenceId), clip.SourceRevisionID, token));
                if (!resolved.IsSuccess) result = MediaEngineResult<PcmWaveformRange>.Failure(resolved.Error!);
                else
                {
                    _source = resolved.Value!;
                    if (!ValidLease(_source, new(clip.SourceReferenceId), clip.SourceFileID, clip.SourceRevisionID))
                        result = Failure<PcmWaveformRange>(MediaEngineErrorCode.RevisionConflict);
                    else if (!string.Equals(await Retain(HashAsync(_source.Source.SourceUri.LocalPath, token)), clip.SourceSha256, StringComparison.OrdinalIgnoreCase))
                        result = Failure<PcmWaveformRange>(MediaEngineErrorCode.RevisionConflict);
                    else
                    {
                        var path = _source.Source.SourceUri.LocalPath;
                        if (clip.AudioDerivation is { } expected)
                        {
                            if (_owner._audioDecoder is null) result = Failure<PcmWaveformRange>(MediaEngineErrorCode.BackendUnavailable);
                            else
                            {
                                var decoded = await Retain(_owner._audioDecoder.DecodeAsync(_source, token));
                                if (!decoded.IsSuccess) result = MediaEngineResult<PcmWaveformRange>.Failure(decoded.Error!);
                                else
                                {
                                    _decoded = decoded.Value!;
                                    if (!ValidDecoded(_decoded, _source, clip.SourceSha256) ||
                                        expected != new WaveAudioDerivation(_decoded.Source.DecodedSha256, _decoded.Source.DecodeProfile, _decoded.Source.Runtime))
                                        result = Failure<PcmWaveformRange>(MediaEngineErrorCode.RevisionConflict);
                                    else path = _decoded.Source.TemporaryWavePath;
                                }
                            }
                        }
                        if (result is null && _decoded is not null &&
                            !string.Equals(await Retain(HashAsync(path, token)), _decoded.Source.DecodedSha256, StringComparison.OrdinalIgnoreCase))
                            result = Failure<PcmWaveformRange>(MediaEngineErrorCode.RevisionConflict);
                        if (result is null)
                        {
                            var analysis = await Retain(Task.Run(() => Haven.Infrastructure.Media.PcmWaveformReader.AnalyzeRange(path,
                                clip.SourceStartFrame, clip.FrameCount, buckets, token), token));
                            if (analysis.SampleRate != project.SampleRate || analysis.Channels.Count != project.Channels ||
                                (_decoded is not null && !string.Equals(await Retain(HashAsync(path, token)), _decoded.Source.DecodedSha256, StringComparison.OrdinalIgnoreCase)) ||
                                !string.Equals(await Retain(HashAsync(_source.Source.SourceUri.LocalPath, token)), clip.SourceSha256, StringComparison.OrdinalIgnoreCase))
                                result = Failure<PcmWaveformRange>(MediaEngineErrorCode.RevisionConflict);
                            else result = MediaEngineResult<PcmWaveformRange>.Success(analysis);
                        }
                    }
                }
            }
            catch (Exception failure) { bodyFailure = failure; }
            // The SAME raw release sources are held and independently joined,
            // even when analysis or the first release failed.
            var failures = new List<Exception>(); if (bodyFailure is not null) failures.Add(bodyFailure);
            if (_decoded is not null)
                try { var release = _decoded.DisposeAsync().AsTask(); _raw.Add(release); await release; }
                catch (Exception failure) { failures.Add(failure); }
            if (_source is not null)
                try { var release = _source.DisposeAsync().AsTask(); _raw.Add(release); await release; }
                catch (Exception failure) { failures.Add(failure); }
            if (failures.Count != 0) throw new AggregateException("Wave retains its original waveform source or release failures.", failures);
            return result ?? throw new InvalidOperationException("Original waveform analysis produced no result.");
        }
    }
}
