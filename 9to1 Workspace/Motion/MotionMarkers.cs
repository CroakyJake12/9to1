namespace HavenOS.Apps.Motion;

/// <summary>Canonical sequence position in integral ticks of its ORIGINAL rational frame timebase.</summary>
public sealed record MotionMarker(Guid MarkerId, long Frame, string Name, long Revision = 0);

public static class MotionMarkers
{
    public const int MaximumMarkersPerSequence = 4096;
    public static void Validate(MotionMarker marker)
    {
        if (marker is null || marker.MarkerId == Guid.Empty || marker.Frame < 0 || marker.Frame == long.MaxValue
            || marker.Revision < 0 || string.IsNullOrWhiteSpace(marker.Name) || marker.Name.Length > 256
            || marker.Name.Any(char.IsControl))
            throw new InvalidDataException("Marker identity, frame, name or revision is invalid.");
    }
    public static long FrameAtPosition(double offset, double viewportWidth, long duration)
    {
        if (!double.IsFinite(offset) || !double.IsFinite(viewportWidth) || viewportWidth <= 0 || duration < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        var rounded = Math.Round(Math.Clamp(offset / viewportWidth, 0, 1) * duration);
        return rounded >= duration ? duration : (long)rounded;
    }
    public static long SnapTolerance(double pixels, double viewportWidth, long duration)
    {
        if (!double.IsFinite(pixels) || !double.IsFinite(viewportWidth) || pixels < 0 || viewportWidth <= 0 || duration < 0)
            throw new ArgumentOutOfRangeException(nameof(pixels));
        var frames = Math.Ceiling(pixels / viewportWidth * duration);
        return frames >= long.MaxValue ? long.MaxValue : Math.Max(1, (long)frames);
    }
    public static long SnapFrame(MotionSequence sequence, long frame, long toleranceFrames,
        long? rangeIn = null, long? rangeOut = null)
    {
        if (frame < 0 || toleranceFrames < 0) throw new ArgumentOutOfRangeException(nameof(frame));
        long best = frame, distance = long.MaxValue;
        void Consider(long candidate)
        {
            if (candidate < 0) return;
            // Both operands are non-negative frame ticks, so this difference cannot overflow.
            var delta = candidate >= frame ? candidate - frame : frame - candidate;
            if (delta <= toleranceFrames && (delta < distance || delta == distance && candidate < best))
            { best = candidate; distance = delta; }
        }
        Consider(0);
        foreach (var marker in sequence.Markers ?? []) Consider(marker.Frame);
        foreach (var clip in sequence.VideoTracks.SelectMany(track => track.Elements))
        { Consider(clip.TimelineStart); Consider(checked(clip.TimelineStart + clip.Duration)); }
        if (rangeIn.HasValue) Consider(rangeIn.Value);
        if (rangeOut.HasValue) Consider(rangeOut.Value);
        return best;
    }
}

public sealed partial class MotionProjectStore
{
    public MotionProject AddMarker(MotionProject project, long expectedRevision, Guid sequenceId, long frame, string name)
        => EditSequence(project, expectedRevision, sequenceId, sequence =>
        {
            var marker = new MotionMarker(Guid.NewGuid(), frame, name); MotionMarkers.Validate(marker);
            if ((sequence.Markers?.Count ?? 0) >= MotionMarkers.MaximumMarkersPerSequence)
                throw new InvalidOperationException("The sequence marker limit was reached.");
            return sequence with { Markers = OrderedMarkers((sequence.Markers ?? []).Append(marker)) };
        });
    public MotionProject UpdateMarker(MotionProject project, long expectedRevision, Guid sequenceId,
        Guid markerId, long expectedMarkerRevision, long frame, string name)
        => EditSequence(project, expectedRevision, sequenceId, sequence =>
        {
            var original = (sequence.Markers ?? []).SingleOrDefault(marker => marker.MarkerId == markerId)
                ?? throw new KeyNotFoundException("MarkerNotFound");
            if (original.Revision != expectedMarkerRevision) throw new InvalidOperationException("MarkerRevisionConflict");
            var updated = original with { Frame = frame, Name = name, Revision = checked(original.Revision + 1) };
            MotionMarkers.Validate(updated);
            return sequence with { Markers = OrderedMarkers(sequence.Markers!.Select(marker => marker.MarkerId == markerId ? updated : marker)) };
        });
    public MotionProject DeleteMarker(MotionProject project, long expectedRevision, Guid sequenceId,
        Guid markerId, long expectedMarkerRevision)
        => EditSequence(project, expectedRevision, sequenceId, sequence =>
        {
            var original = (sequence.Markers ?? []).SingleOrDefault(marker => marker.MarkerId == markerId)
                ?? throw new KeyNotFoundException("MarkerNotFound");
            if (original.Revision != expectedMarkerRevision) throw new InvalidOperationException("MarkerRevisionConflict");
            return sequence with { Markers = sequence.Markers!.Where(marker => marker.MarkerId != markerId).ToArray() };
        });
    private static MotionMarker[] OrderedMarkers(IEnumerable<MotionMarker> markers)
        => markers.OrderBy(marker => marker.Frame).ThenBy(marker => marker.MarkerId).ToArray();
}
