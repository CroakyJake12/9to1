namespace HavenOS.Apps.Motion;

public enum MotionTrimEdge { Start, End }

public sealed partial class MotionProjectStore
{
    public MotionProject MoveToTrack(MotionProject project, long expectedRevision, Guid sequenceId,
        Guid elementId, Guid destinationTrackId, long timelineStart)
        => EditSequence(project, expectedRevision, sequenceId, sequence =>
        {
            var source = sequence.VideoTracks.SingleOrDefault(t => t.Elements.Any(e => e.ElementId == elementId))
                ?? throw new KeyNotFoundException("ElementNotFound");
            var destination = sequence.VideoTracks.SingleOrDefault(t => t.TrackId == destinationTrackId)
                ?? throw new KeyNotFoundException("TrackNotFound");
            EnsureEditable(source); EnsureEditable(destination);
            var original = source.Elements.Single(e => e.ElementId == elementId);
            var moved = FromShared(original, Haven.Core.Media.MediaTimelineEdits.Move(ToShared(sequence, original), Timebase(sequence).At(timelineStart)))
                with { TrackId = destinationTrackId };
            return sequence with { VideoTracks = sequence.VideoTracks.Select(t => t with
            {
                Elements = Ordered(t.Elements.Where(e => e.ElementId != elementId).Concat(t.TrackId == destinationTrackId ? new[] { moved } : Array.Empty<MotionElement>()))
            }).ToArray() };
        });

    /// <summary>Moves one cut while preserving both outer timeline boundaries. Source handles remain non-destructive.</summary>
    public MotionProject Roll(MotionProject project, long expectedRevision, Guid sequenceId,
        Guid leftElementId, Guid rightElementId, long cutFrame)
        => EditSequence(project, expectedRevision, sequenceId, sequence =>
        {
            var track = sequence.VideoTracks.SingleOrDefault(t => t.Elements.Any(e => e.ElementId == leftElementId))
                ?? throw new KeyNotFoundException("ElementNotFound");
            EnsureEditable(track);
            var left = track.Elements.Single(e => e.ElementId == leftElementId);
            var right = track.Elements.SingleOrDefault(e => e.ElementId == rightElementId)
                ?? throw new InvalidOperationException("Roll requires two clips on the same track.");
            if (left.ElementId == right.ElementId || left.TimelineStart + left.Duration != right.TimelineStart)
                throw new InvalidOperationException("Roll requires adjacent clips with a shared cut.");
            if (cutFrame <= left.TimelineStart || cutFrame >= right.TimelineStart + right.Duration)
                throw new ArgumentOutOfRangeException(nameof(cutFrame));
            var delta = cutFrame - right.TimelineStart;
            var newLeft = left with { Duration = checked(left.Duration + delta), SourceOut = checked(left.SourceOut + delta) };
            var newRight = right with { TimelineStart = cutFrame, Duration = checked(right.Duration - delta), SourceIn = checked(right.SourceIn + delta) };
            if (newRight.SourceIn < 0) throw new ArgumentOutOfRangeException(nameof(cutFrame), "The roll would move before the source begins.");
            return sequence with { VideoTracks = sequence.VideoTracks.Select(t => t.TrackId != track.TrackId ? t : t with
            { Elements = Ordered(t.Elements.Select(e => e.ElementId == leftElementId ? newLeft : e.ElementId == rightElementId ? newRight : e)) }).ToArray() };
        });

    /// <summary>Slides a clip between its touching neighbors, retaining its source range and the neighbors' outer boundaries.</summary>
    public MotionProject Slide(MotionProject project, long expectedRevision, Guid sequenceId, Guid elementId, long timelineStart)
        => EditSequence(project, expectedRevision, sequenceId, sequence =>
        {
            var track = sequence.VideoTracks.SingleOrDefault(t => t.Elements.Any(e => e.ElementId == elementId))
                ?? throw new KeyNotFoundException("ElementNotFound");
            EnsureEditable(track);
            var current = track.Elements.Single(e => e.ElementId == elementId);
            var left = track.Elements.SingleOrDefault(e => e.ElementId != elementId && e.TimelineStart + e.Duration == current.TimelineStart);
            var right = track.Elements.SingleOrDefault(e => e.ElementId != elementId && e.TimelineStart == current.TimelineStart + current.Duration);
            if (left is null || right is null) throw new InvalidOperationException("Slide requires a touching clip on each side.");
            var delta = checked(timelineStart - current.TimelineStart);
            var newLeft = left with { Duration = checked(left.Duration + delta), SourceOut = checked(left.SourceOut + delta) };
            var newRight = right with { TimelineStart = checked(right.TimelineStart + delta), Duration = checked(right.Duration - delta), SourceIn = checked(right.SourceIn + delta) };
            if (newLeft.Duration <= 0 || newRight.Duration <= 0 || newRight.SourceIn < 0)
                throw new ArgumentOutOfRangeException(nameof(timelineStart), "The slide must leave positive neighboring clips and valid source handles.");
            var moved = current with { TimelineStart = timelineStart };
            return sequence with { VideoTracks = sequence.VideoTracks.Select(t => t.TrackId != track.TrackId ? t : t with
            { Elements = Ordered(t.Elements.Select(e => e.ElementId == left.ElementId ? newLeft : e.ElementId == right.ElementId ? newRight : e.ElementId == elementId ? moved : e)) }).ToArray() };
        });

    /// <summary>Inward ripple trimming removes the selected edge's time across the sequence, including caption timing.</summary>
    public MotionProject RippleTrim(MotionProject project, long expectedRevision, Guid sequenceId,
        Guid elementId, MotionTrimEdge edge, long sourceBoundary)
    {
        Validate(project); EnsureRevision(project, expectedRevision);
        var sequence = project.Sequences.SingleOrDefault(s => s.SequenceId == sequenceId) ?? throw new KeyNotFoundException("SequenceNotFound");
        var track = sequence.VideoTracks.SingleOrDefault(t => t.Elements.Any(e => e.ElementId == elementId)) ?? throw new KeyNotFoundException("ElementNotFound");
        EnsureEditable(track); var element = track.Elements.Single(e => e.ElementId == elementId);
        return edge switch
        {
            MotionTrimEdge.Start when sourceBoundary > element.SourceIn && sourceBoundary < element.SourceOut =>
                RemoveRange(project, expectedRevision, sequenceId, element.TimelineStart,
                    checked(element.TimelineStart + (sourceBoundary - element.SourceIn)), true),
            MotionTrimEdge.End when sourceBoundary > element.SourceIn && sourceBoundary < element.SourceOut =>
                RemoveRange(project, expectedRevision, sequenceId, checked(element.TimelineStart + (sourceBoundary - element.SourceIn)),
                    checked(element.TimelineStart + element.Duration), true),
            _ => throw new ArgumentOutOfRangeException(nameof(sourceBoundary), "Ripple trim must narrow the selected source range.")
        };
    }
}
