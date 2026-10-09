using Haven.Core.Media;

namespace HavenOS.Apps.Motion;

public sealed partial class MotionProjectStore
{
    public static MediaTimebase Timebase(MotionSequence sequence) => MediaTimebase.FramesPerSecond(sequence.FrameRateNumerator, sequence.FrameRateDenominator);
    public static MediaTimelineClip ToShared(MotionSequence sequence, MotionElement element)
    {
        var clock = Timebase(sequence);
        return new(element.ElementId, element.TrackId, new(element.AssetId), clock.At(element.SourceIn), clock.At(element.Duration), clock.At(element.TimelineStart));
    }
    private static MotionElement FromShared(MotionElement original, MediaTimelineClip clip) => original with
    { ElementId = clip.ClipId, TrackId = clip.TrackId, AssetId = clip.AssetId.Value, TimelineStart = clip.TimelineStart.Ticks,
        Duration = clip.Duration.Ticks, SourceIn = clip.SourceStart.Ticks, SourceOut = checked(clip.SourceStart.Ticks + clip.Duration.Ticks) };
    private static void EnsureEditable(MotionTrack track)
    { if (track.Locked) throw new InvalidOperationException("TrackLocked"); }
    private static IReadOnlyList<MotionElement> Ordered(IEnumerable<MotionElement> elements) => elements.OrderBy(e => e.TimelineStart).ThenBy(e => e.ElementId).ToArray();
    private static IReadOnlyList<MotionElement> InsertAt(MotionSequence sequence, MotionTrack track, MotionElement inserted)
    {
        var output = new List<MotionElement>();
        foreach (var item in track.Elements)
        {
            if (item.TimelineStart >= inserted.TimelineStart)
                output.Add(item with { TimelineStart = checked(item.TimelineStart + inserted.Duration) });
            else if (checked(item.TimelineStart + item.Duration) > inserted.TimelineStart)
            {
                var split = MediaTimelineEdits.Split(ToShared(sequence, item), Timebase(sequence).At(inserted.TimelineStart));
                output.Add(FromShared(item, split.Left));
                output.Add(FromShared(item, split.Right) with { TimelineStart = checked(inserted.TimelineStart + inserted.Duration) });
            }
            else output.Add(item);
        }
        output.Add(inserted);
        return Ordered(output);
    }
    private static MotionProject EditSequence(MotionProject project, long expectedRevision, Guid sequenceId, Func<MotionSequence, MotionSequence> change)
    {
        Validate(project); EnsureRevision(project, expectedRevision);
        var index = IndexOf(project.Sequences, sequenceId, s => s.SequenceId, "SequenceNotFound");
        var sequences = project.Sequences.ToArray(); sequences[index] = change(sequences[index]);
        var result = project with { Sequences = sequences, Revision = checked(project.Revision + 1), ModifiedAt = DateTimeOffset.UtcNow };
        Validate(result); return result;
    }
    public MotionProject AddTrack(MotionProject project, long expectedRevision, Guid sequenceId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return EditSequence(project, expectedRevision, sequenceId, s => s with { VideoTracks = s.VideoTracks.Append(new MotionTrack(Guid.NewGuid(), name, [])).ToArray() });
    }
    public MotionProject SetTrackState(MotionProject project, long expectedRevision, Guid sequenceId, Guid trackId, bool locked, bool visible)
        => EditSequence(project, expectedRevision, sequenceId, s =>
        {
            _ = IndexOf(s.VideoTracks, trackId, t => t.TrackId, "TrackNotFound");
            return s with { VideoTracks = s.VideoTracks.Select(t => t.TrackId == trackId ? t with { Locked = locked, Visible = visible } : t).ToArray() };
        });
    public MotionProject AddAsset(MotionProject project, long expectedRevision, MotionAssetReference asset)
    {
        EnsureRevision(project, expectedRevision);
        var result = project with { AssetReferences = project.AssetReferences.Append(asset).ToArray(), Revision = checked(project.Revision + 1), ModifiedAt = DateTimeOffset.UtcNow };
        Validate(result); return result;
    }
    public MotionProject RelinkAsset(MotionProject project, long expectedRevision, Guid assetId, string fileId, string sourceRevision)
    {
        Validate(project); EnsureRevision(project, expectedRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId); ArgumentException.ThrowIfNullOrWhiteSpace(sourceRevision);
        if (!project.AssetReferences.Any(a => a.AssetId == assetId)) throw new KeyNotFoundException("AssetNotFound");
        var result = project with { AssetReferences = project.AssetReferences.Select(a => a.AssetId == assetId
            ? a with { FileId = fileId, SourceRevisionID = sourceRevision } : a).ToArray(),
            Revision = checked(project.Revision + 1), ModifiedAt = DateTimeOffset.UtcNow };
        Validate(result); return result;
    }
    public MotionProject Overwrite(MotionProject project, long expectedRevision, Guid sequenceId, Guid trackId, Guid assetId, long start, long sourceIn, long sourceOut)
        => EditSequence(project, expectedRevision, sequenceId, s =>
        {
            if (start < 0 || sourceIn < 0 || sourceOut <= sourceIn) throw new ArgumentOutOfRangeException(nameof(start));
            var duration = checked(sourceOut - sourceIn); var end = checked(start + duration);
            var i = IndexOf(s.VideoTracks, trackId, t => t.TrackId, "TrackNotFound"); var track = s.VideoTracks[i]; EnsureEditable(track);
            var elements = new List<MotionElement>();
            foreach (var item in track.Elements)
            {
                var itemEnd = checked(item.TimelineStart + item.Duration);
                if (itemEnd <= start || item.TimelineStart >= end) { elements.Add(item); continue; }
                if (item.TimelineStart < start) elements.Add(item with { Duration = start - item.TimelineStart, SourceOut = checked(item.SourceIn + (start - item.TimelineStart)) });
                if (itemEnd > end) elements.Add(item with { ElementId = item.TimelineStart < start ? Guid.NewGuid() : item.ElementId,
                    TimelineStart = end, SourceIn = checked(item.SourceIn + (end - item.TimelineStart)), Duration = itemEnd - end });
            }
            elements.Add(new(Guid.NewGuid(), trackId, assetId, start, duration, sourceIn, sourceOut));
            return s with { VideoTracks = s.VideoTracks.Select(t => t.TrackId == trackId ? t with { Elements = Ordered(elements) } : t).ToArray() };
        });
    /// <summary>Lift/extract an explicit sequence range on all tracks. Extract closes time on every editable track and captions.</summary>
    public MotionProject RemoveRange(MotionProject project, long expectedRevision, Guid sequenceId, long start, long end, bool ripple)
        => EditSequence(project, expectedRevision, sequenceId, s =>
        {
            if (start < 0 || end <= start) throw new ArgumentOutOfRangeException(nameof(start));
            var amount = end - start;
            var tracks = s.VideoTracks.Select(track =>
            {
                var affected = track.Elements.Any(e => e.TimelineStart + e.Duration > start && (ripple || e.TimelineStart < end));
                if (affected) EnsureEditable(track);
                var elements = new List<MotionElement>();
                foreach (var item in track.Elements)
                {
                    var itemEnd = item.TimelineStart + item.Duration;
                    if (itemEnd <= start) { elements.Add(item); continue; }
                    if (item.TimelineStart >= end) { elements.Add(ripple ? item with { TimelineStart = item.TimelineStart - amount } : item); continue; }
                    if (item.TimelineStart < start) elements.Add(item with { Duration = start - item.TimelineStart, SourceOut = item.SourceIn + (start - item.TimelineStart) });
                    if (itemEnd > end) elements.Add(item with { ElementId = item.TimelineStart < start ? Guid.NewGuid() : item.ElementId,
                        TimelineStart = ripple ? start : end, SourceIn = item.SourceIn + (end - item.TimelineStart), Duration = itemEnd - end });
                }
                return track with { Elements = Ordered(elements) };
            }).ToArray();
            var captions = (s.CaptionTracks ?? []).Select(track => track with { Cues = track.Cues.SelectMany(cue =>
            {
                if (cue.EndFrame <= start) return new[] { cue };
                if (cue.StartFrame >= end) return new[] { ripple ? cue with { StartFrame = cue.StartFrame - amount, EndFrame = cue.EndFrame - amount } : cue };
                var fragments = new List<MotionCaptionCue>();
                if (cue.StartFrame < start) fragments.Add(cue with { EndFrame = start });
                if (cue.EndFrame > end) fragments.Add(cue with { CueId = cue.StartFrame < start ? Guid.NewGuid() : cue.CueId, StartFrame = ripple ? start : end, EndFrame = ripple ? cue.EndFrame - amount : cue.EndFrame });
                return fragments.ToArray();
            }).ToArray() }).ToArray();
            return s with { VideoTracks = tracks, CaptionTracks = captions };
        });
    public MotionProject Delete(MotionProject project, long expectedRevision, Guid sequenceId, Guid elementId, bool ripple)
    {
        var sequence = project.Sequences.Single(s => s.SequenceId == sequenceId);
        var track = sequence.VideoTracks.Single(t => t.Elements.Any(e => e.ElementId == elementId)); EnsureEditable(track);
        var element = track.Elements.Single(e => e.ElementId == elementId);
        if (ripple) return RemoveRange(project, expectedRevision, sequenceId, element.TimelineStart, element.TimelineStart + element.Duration, true);
        return EditSequence(project, expectedRevision, sequenceId, s => s with { VideoTracks = s.VideoTracks.Select(t => t with { Elements = t.Elements.Where(e => e.ElementId != elementId).ToArray() }).ToArray() });
    }
    public MotionProject Duplicate(MotionProject project, long expectedRevision, Guid sequenceId, Guid elementId)
        => EditSequence(project, expectedRevision, sequenceId, s =>
        {
            if (!s.VideoTracks.Any(t => t.Elements.Any(e => e.ElementId == elementId))) throw new KeyNotFoundException("ElementNotFound");
            return s with { VideoTracks = s.VideoTracks.Select(track =>
            {
                var item = track.Elements.SingleOrDefault(e => e.ElementId == elementId); if (item is null) return track;
                EnsureEditable(track); return track with { Elements = InsertAt(s, track, item with { ElementId = Guid.NewGuid(), TimelineStart = checked(item.TimelineStart + item.Duration) }) };
            }).ToArray() };
        });
    public MotionProject SetCaptions(MotionProject project, long expectedRevision, Guid sequenceId, MotionCaptionTrack track)
        => EditSequence(project, expectedRevision, sequenceId, s => s with
        { CaptionTracks = (s.CaptionTracks ?? []).Where(t => t.TrackId != track.TrackId).Append(track).ToArray() });
}

/// <summary>Autosaved revision-aware editing. Undo/redo creates a new revision; it never rewinds the CAS clock.</summary>
public sealed class MotionEditSession
{
    private readonly MotionProjectStore _store;
    private readonly Stack<MotionProject> _undo = [], _redo = [];
    public string Path { get; }
    public MotionProject Project { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public MotionEditSession(MotionProjectStore store, string path) { _store = store; Path = path; Project = store.Load(path); }
    public void Apply(Func<MotionProject, MotionProject> edit)
    {
        var before = Project; var next = edit(before); _store.Save(Path, next, before.Revision);
        _undo.Push(before); _redo.Clear(); Project = next;
    }
    public void Reload() { var current = _store.Load(Path); Project = current; _undo.Clear(); _redo.Clear(); }
    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);
    private void Restore(Stack<MotionProject> source, Stack<MotionProject> target)
    {
        if (source.Count == 0) throw new InvalidOperationException("NoHistory");
        var before = Project; var restored = source.Peek() with { Revision = checked(before.Revision + 1), ModifiedAt = DateTimeOffset.UtcNow };
        _store.Save(Path, restored, before.Revision); source.Pop(); target.Push(before); Project = restored;
    }
}
