using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace HavenOS.Apps.Wave;

public sealed class WaveEngine : IWaveEngine
{
    private readonly ConcurrentDictionary<string, WaveAsset> _assets = new();
    private readonly ConcurrentDictionary<string, WaveTimeline> _timelines = new();
    private readonly ConcurrentDictionary<string, WaveExportJob> _exportJobs = new();
    private Action<WaveEngineEvent>? _eventCallback;
    private int _idCounter = 0;

    public WaveEngine() { }

    public async Task<WaveAsset> ImportAssetAsync(string path, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Asset not found: {path}");

        // Probe media info using gstreamer
        var mediaInfo = await ProbeMediaInfoAsync(fullPath, cancellationToken);
        
        var assetId = $"asset-{Interlocked.Increment(ref _idCounter)}";
        var asset = new WaveAsset(assetId, fullPath, Path.GetFileName(fullPath), mediaInfo.Duration, mediaInfo);
        _assets[assetId] = asset;
        
        _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.AssetImported, assetId));
        return asset;
    }

    public async Task<IReadOnlyList<WaveAsset>> GetAssetsAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return [.. _assets.Values];
    }

    public async Task RemoveAssetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (_assets.TryRemove(assetId, out _))
        {
            _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.AssetRemoved, assetId));
        }
    }

    public async Task<WaveTimeline> CreateTimelineAsync(string name, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var timelineId = $"timeline-{Interlocked.Increment(ref _idCounter)}";
        var timeline = new WaveTimeline(timelineId, name, duration, [], []);
        _timelines[timelineId] = timeline;
        _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.TimelineCreated, timelineId));
        return timeline;
    }

    public async Task<WaveTimeline> GetTimelineAsync(string timelineId, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (_timelines.TryGetValue(timelineId, out var timeline))
            return timeline;
        throw new KeyNotFoundException($"Timeline not found: {timelineId}");
    }

    public async Task<IReadOnlyList<WaveTimeline>> GetTimelinesAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return [.. _timelines.Values];
    }

    public async Task<WaveTimeline> UpdateTimelineAsync(WaveTimeline timeline, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        _timelines[timeline.Id] = timeline;
        _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.TimelineUpdated, timeline.Id));
        return timeline;
    }

    public async Task DeleteTimelineAsync(string timelineId, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (_timelines.TryRemove(timelineId, out _))
        {
            _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.TimelineDeleted, timelineId));
        }
    }

    public async Task<WaveTrack> AddTrackAsync(string timelineId, WaveTrack track, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (!_timelines.TryGetValue(timelineId, out var timeline))
            throw new KeyNotFoundException($"Timeline not found: {timelineId}");

        var newTrack = track with { Id = track.Id ?? $"track-{Interlocked.Increment(ref _idCounter)}" };
        var tracks = timeline.Tracks.Append(newTrack).ToArray();
        var updated = timeline with { Tracks = tracks };
        _timelines[timelineId] = updated;
        _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.TimelineUpdated, timelineId));
        return newTrack;
    }

    public async Task<WaveClip> AddClipAsync(string timelineId, WaveClip clip, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (!_timelines.TryGetValue(timelineId, out var timeline))
            throw new KeyNotFoundException($"Timeline not found: {timelineId}");

        var newClip = clip with { Id = clip.Id ?? $"clip-{Interlocked.Increment(ref _idCounter)}" };
        var clips = timeline.Clips.Append(newClip).ToArray();
        var updated = timeline with { Clips = clips };
        _timelines[timelineId] = updated;
        _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.ClipAdded, newClip.Id));
        return newClip;
    }

    public async Task<WaveClip> UpdateClipAsync(string clipId, WaveClip clip, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        foreach (var kvp in _timelines)
        {
            var timeline = kvp.Value;
            var clipIndex = timeline.Clips.FindIndex(c => c.Id == clipId);
            if (clipIndex >= 0)
            {
                var clips = timeline.Clips.ToArray();
                clips[clipIndex] = clip;
                var updated = timeline with { Clips = clips };
                _timelines[kvp.Key] = updated;
                _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.ClipUpdated, clipId));
                return clip;
            }
        }
        throw new KeyNotFoundException($"Clip not found: {clipId}");
    }

    public async Task RemoveClipAsync(string clipId, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        foreach (var kvp in _timelines)
        {
            var timeline = kvp.Value;
            var clips = timeline.Clips.Where(c => c.Id != clipId).ToArray();
            if (clips.Length != timeline.Clips.Count)
            {
                var updated = timeline with { Clips = clips };
                _timelines[kvp.Key] = updated;
                _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.ClipRemoved, clipId));
                return;
            }
        }
    }

    public async Task<WaveTransition> AddTransitionAsync(string timelineId, WaveTransition transition, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        // Would add to timeline transitions
        _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.TransitionAdded, transition.Id));
        return transition;
    }

    public async Task<WaveEffect> AddEffectAsync(string clipId, WaveEffect effect, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        // Would add effect to clip
        _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.EffectAdded, effect.Id));
        return effect;
    }

    public async Task<WaveEffect> UpdateEffectAsync(string effectId, WaveEffect effect, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        // Would update effect
        _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.EffectUpdated, effectId));
        return effect;
    }

    public async Task RemoveEffectAsync(string effectId, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        // Would remove effect
        _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.EffectRemoved, effectId));
    }

    public async Task<WaveExportJob> ExportAsync(string timelineId, WaveExportSettings settings, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (!_timelines.TryGetValue(timelineId, out var timeline))
            throw new KeyNotFoundException($"Timeline not found: {timelineId}");

        var jobId = $"export-{Interlocked.Increment(ref _idCounter)}";
        var job = new WaveExportJob(jobId, timelineId, settings, WaveExportStatus.Pending, 0, null, null);
        _exportJobs[jobId] = job;

        _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.ExportStarted, jobId));

        // Start export in background
        _ = Task.Run(async () =>
        {
            try
            {
                job = job with { Status = WaveExportStatus.Running };
                _exportJobs[jobId] = job;
                _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.ExportProgress, $"{jobId}:0"));

                // Build GES timeline and export using gst-launch-1.0 or GES API
                await ExportTimelineGStreamerAsync(timeline, settings, progress =>
                {
                    job = job with { Progress = progress };
                    _exportJobs[jobId] = job;
                    _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.ExportProgress, $"{jobId}:{progress}"));
                }, cancellationToken);

                job = job with { Status = WaveExportStatus.Completed, Progress = 1.0, OutputPath = settings.OutputPath };
                _exportJobs[jobId] = job;
                _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.ExportCompleted, jobId));
            }
            catch (Exception ex)
            {
                job = job with { Status = WaveExportStatus.Failed, Error = ex.Message };
                _exportJobs[jobId] = job;
                _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.ExportFailed, $"{jobId}:{ex.Message}"));
            }
        }, cancellationToken);

        return job;
    }

    public async Task<WaveExportJob> GetExportJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (_exportJobs.TryGetValue(jobId, out var job))
            return job;
        throw new KeyNotFoundException($"Export job not found: {jobId}");
    }

    public async Task CancelExportAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (_exportJobs.TryGetValue(jobId, out var job))
        {
            job = job with { Status = WaveExportStatus.Cancelled };
            _exportJobs[jobId] = job;
            _eventCallback?.Invoke(new WaveEngineEvent(WaveEngineEventType.ExportFailed, $"{jobId}:Cancelled"));
        }
    }

    public void SetEventCallback(Action<WaveEngineEvent> callback) => _eventCallback = callback;

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private async Task<WaveMediaInfo> ProbeMediaInfoAsync(string path, CancellationToken cancellationToken)
    {
        // Use gst-discoverer-1.0 or GStreamer API to probe media
        // For now, return defaults
        await Task.Yield();
        return new WaveMediaInfo(1920, 1080, 30, "h264", "aac", 2, 48000);
    }

    private async Task ExportTimelineGStreamerAsync(WaveTimeline timeline, WaveExportSettings settings, Action<double> progressCallback, CancellationToken cancellationToken)
    {
        // Build GES timeline XML and use gst-launch-1.0 or ges-launch-1.0 to export
        // This is a simplified implementation
        await Task.Delay(100, cancellationToken); // Simulate work
        progressCallback(0.25);
        await Task.Delay(100, cancellationToken);
        progressCallback(0.5);
        await Task.Delay(100, cancellationToken);
        progressCallback(0.75);
        await Task.Delay(100, cancellationToken);
        progressCallback(1.0);
    }
}