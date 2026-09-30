using Haven.Core.Media;

namespace HavenOS.Apps.Wave;

/// <summary>Shared media range edits retain the source asset, hash and source bytes.</summary>
public static class WaveProjectEdits
{
    public static WaveProject AddTrack(WaveProject project, long expectedRevision, string name)
    {
        EnsureRevision(project, expectedRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Commit(project, [.. project.Tracks, new(Guid.NewGuid(), name.Trim(), [])]);
    }

    public static WaveProject Split(WaveProject project, long expectedRevision, Guid clipId, long timelineFrame) =>
        Edit(project, expectedRevision, clipId, clip =>
        {
            var offset = timelineFrame - clip.TimelineStartFrame;
            if (offset < clip.FadeInFrames || offset > clip.FrameCount - clip.FadeOutFrames)
                throw new NotSupportedException("Split inside an active fade requires envelope subdivision; move the split beyond the fade or edit the fade first.");
            var split = MediaTimelineEdits.Split(ProjectClip(project, clip), MediaTimebase.SamplesPerSecond(project.SampleRate).At(timelineFrame));
            return [Apply(clip, split.Left) with { FadeOutFrames = 0 }, Apply(clip, split.Right) with { FadeInFrames = 0, FadeOutFrames = clip.FadeOutFrames }];
        });

    public static WaveProject Trim(WaveProject project, long expectedRevision, Guid clipId, long startFrames, long endFrames) =>
        Edit(project, expectedRevision, clipId, clip =>
        {
            var projected = ProjectClip(project, clip);
            var timebase = projected.Duration.Timebase;
            if (startFrames < 0 || endFrames < 0) throw new ArgumentOutOfRangeException(nameof(startFrames));
            if (startFrames > 0) projected = MediaTimelineEdits.TrimStart(projected, timebase.At(startFrames));
            if (endFrames > 0) projected = MediaTimelineEdits.TrimEnd(projected, timebase.At(endFrames));
            return [Apply(clip, projected)];
        });

    public static WaveProject Move(WaveProject project, long expectedRevision, Guid clipId, long timelineFrame) =>
        Edit(project, expectedRevision, clipId, clip => [Apply(clip, MediaTimelineEdits.Move(ProjectClip(project, clip),
            MediaTimebase.SamplesPerSecond(project.SampleRate).At(timelineFrame)))]);

    public static WaveProject Duplicate(WaveProject project, long expectedRevision, Guid clipId, long timelineFrame) =>
        Edit(project, expectedRevision, clipId, clip => [clip, Apply(clip, MediaTimelineEdits.Move(ProjectClip(project, clip),
            MediaTimebase.SamplesPerSecond(project.SampleRate).At(timelineFrame))) with { ClipId = Guid.NewGuid() }]);

    public static WaveProject SetClipProcessing(WaveProject project, long expectedRevision, Guid clipId, double gain, long fadeInFrames, long fadeOutFrames) =>
        Edit(project, expectedRevision, clipId, clip =>
        {
            if (!double.IsFinite(gain) || gain is < 0 or > 64 || fadeInFrames < 0 || fadeOutFrames < 0
                || fadeInFrames > clip.FrameCount || fadeOutFrames > clip.FrameCount)
                throw new ArgumentOutOfRangeException(nameof(gain));
            return [clip with { Gain = gain, FadeInFrames = fadeInFrames, FadeOutFrames = fadeOutFrames }];
        });

    public static WaveProject SetTrackMixer(WaveProject project, long expectedRevision, Guid trackId, double gain, double pan, bool mute, bool solo)
    {
        EnsureRevision(project, expectedRevision);
        if (!double.IsFinite(gain) || gain is < 0 or > 64 || !double.IsFinite(pan) || pan is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(gain));
        if (!project.Tracks.Any(track => track.TrackId == trackId)) throw new KeyNotFoundException("TrackNotFound");
        return Commit(project, project.Tracks.Select(track => track.TrackId == trackId
            ? track with { Gain = gain, Pan = pan, Mute = mute, Solo = solo } : track).ToList());
    }

    public static WaveProject Delete(WaveProject project, long expectedRevision, Guid clipId, bool ripple) =>
        Edit(project, expectedRevision, clipId, _ => [], ripple);

    private static WaveProject Edit(WaveProject project, long expectedRevision, Guid clipId,
        Func<WaveClip, IReadOnlyList<WaveClip>> edit, bool ripple = false)
    {
        EnsureRevision(project, expectedRevision);
        var target = project.Tracks.SelectMany(track => track.Clips).SingleOrDefault(clip => clip.ClipId == clipId)
            ?? throw new KeyNotFoundException("ClipNotFound");
        var replacement = edit(target);
        var tracks = project.Tracks.Select(track => track with { Clips = track.Clips.SelectMany(clip => clip.ClipId == clipId ? replacement
            : new[] { ripple && clip.TimelineStartFrame >= checked(target.TimelineStartFrame + target.FrameCount)
                ? clip with { TimelineStartFrame = clip.TimelineStartFrame - target.FrameCount } : clip }).ToList() }).ToList();
        return Commit(project, tracks);
    }

    private static MediaTimelineClip ProjectClip(WaveProject project, WaveClip clip)
    {
        var track = project.Tracks.Single(track => track.Clips.Any(candidate => candidate.ClipId == clip.ClipId));
        var timebase = MediaTimebase.SamplesPerSecond(project.SampleRate);
        return new(clip.ClipId, track.TrackId, new(clip.SourceReferenceId), timebase.At(clip.SourceStartFrame),
            timebase.At(clip.FrameCount), timebase.At(clip.TimelineStartFrame));
    }

    private static WaveClip Apply(WaveClip clip, MediaTimelineClip projected) => clip with { ClipId = projected.ClipId,
        SourceStartFrame = projected.SourceStart.Ticks, FrameCount = projected.Duration.Ticks, TimelineStartFrame = projected.TimelineStart.Ticks,
        FadeInFrames = Math.Min(clip.FadeInFrames, projected.Duration.Ticks), FadeOutFrames = Math.Min(clip.FadeOutFrames, projected.Duration.Ticks) };

    private static WaveProject Commit(WaveProject project, List<WaveTrack> tracks) =>
        project with { Tracks = tracks, Revision = checked(project.Revision + 1), ModifiedAt = DateTimeOffset.UtcNow };

    private static void EnsureRevision(WaveProject project, long revision)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Revision != revision) throw new InvalidOperationException("RevisionConflict");
    }
}
