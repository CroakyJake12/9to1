using Haven.Core.Media;

namespace HavenOS.Apps.Wave;

public sealed record WaveTimelineReference(string Owner, string Kind, Guid Id);
public sealed record WaveMarker(Guid MarkerId, long Frame, string Name, string? Colour = null,
    string? Category = null, string? Notes = null, WaveTimelineReference? Reference = null);
public sealed record WaveRegion(Guid RegionId, long StartFrame, long FrameCount, string Name,
    string? Colour = null, string? Category = null, string? Notes = null, WaveTimelineReference? Reference = null);

/// <summary>Revisioned project annotations. No source audio or clip ranges are changed by annotation edits.</summary>
public static class WaveTimelineAnnotations
{
    public static WaveProject AddMarker(WaveProject project, long expectedRevision, long frame, string name,
        string? colour = null, string? category = null, string? notes = null, WaveTimelineReference? reference = null) =>
        Commit(project, expectedRevision, [.. project.Markers, new(Guid.NewGuid(), frame, name, colour, category, notes, reference)], project.Regions);

    public static WaveProject UpdateMarker(WaveProject project, long expectedRevision, WaveMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        if (!project.Markers.Any(item => item.MarkerId == marker.MarkerId)) throw new KeyNotFoundException("MarkerNotFound");
        return Commit(project, expectedRevision, project.Markers.Select(item => item.MarkerId == marker.MarkerId ? marker : item).ToList(), project.Regions);
    }

    public static WaveProject RemoveMarker(WaveProject project, long expectedRevision, Guid markerId)
    {
        if (!project.Markers.Any(item => item.MarkerId == markerId)) throw new KeyNotFoundException("MarkerNotFound");
        return Commit(project, expectedRevision, project.Markers.Where(item => item.MarkerId != markerId).ToList(), project.Regions);
    }

    public static WaveProject AddRegion(WaveProject project, long expectedRevision, long startFrame, long frameCount, string name,
        string? colour = null, string? category = null, string? notes = null, WaveTimelineReference? reference = null) =>
        Commit(project, expectedRevision, project.Markers, [.. project.Regions, new(Guid.NewGuid(), startFrame, frameCount, name, colour, category, notes, reference)]);

    public static WaveProject UpdateRegion(WaveProject project, long expectedRevision, WaveRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (!project.Regions.Any(item => item.RegionId == region.RegionId)) throw new KeyNotFoundException("RegionNotFound");
        return Commit(project, expectedRevision, project.Markers, project.Regions.Select(item => item.RegionId == region.RegionId ? region : item).ToList());
    }

    public static WaveProject RemoveRegion(WaveProject project, long expectedRevision, Guid regionId)
    {
        if (!project.Regions.Any(item => item.RegionId == regionId)) throw new KeyNotFoundException("RegionNotFound");
        return Commit(project, expectedRevision, project.Markers, project.Regions.Where(item => item.RegionId != regionId).ToList());
    }

    public static MediaTimeRange RegionRange(WaveProject project, Guid regionId)
    {
        WaveProjectStore.Validate(project);
        var region = project.Regions.SingleOrDefault(item => item.RegionId == regionId) ?? throw new KeyNotFoundException("RegionNotFound");
        var timebase = MediaTimebase.SamplesPerSecond(project.SampleRate);
        return new(timebase.At(region.StartFrame), timebase.At(region.FrameCount));
    }

    /// <summary>Strict previous/next boundary; co-located annotations share one stop, with no implicit wrap.</summary>
    public static long? Navigate(WaveProject project, long frame, bool forward)
    {
        WaveProjectStore.Validate(project);
        if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));
        var boundaries = project.Markers.Select(item => item.Frame)
            .Concat(project.Regions.SelectMany(item => new[] { item.StartFrame, checked(item.StartFrame + item.FrameCount) })).Distinct();
        return forward ? boundaries.Where(item => item > frame).Order().Select(item => (long?)item).FirstOrDefault()
            : boundaries.Where(item => item < frame).OrderDescending().Select(item => (long?)item).FirstOrDefault();
    }

    public static void Validate(WaveProject project)
    {
        if (project.Markers is null || project.Regions is null || project.Markers.Count > 10000 || project.Regions.Count > 10000)
            throw new InvalidDataException("Wave annotation collections are unavailable or exceed capacity.");
        if (project.Markers.Any(item => item is null || item.MarkerId == Guid.Empty || item.Frame < 0 || !MetadataValid(item.Name, item.Colour, item.Category, item.Notes, item.Reference))
            || project.Regions.Any(item => item is null || item.RegionId == Guid.Empty || item.StartFrame < 0 || item.FrameCount <= 0
                || item.StartFrame > long.MaxValue - item.FrameCount || !MetadataValid(item.Name, item.Colour, item.Category, item.Notes, item.Reference)))
            throw new InvalidDataException("Wave annotation identity, range or metadata is invalid.");
        if (project.Markers.Select(item => item.MarkerId).Distinct().Count() != project.Markers.Count
            || project.Regions.Select(item => item.RegionId).Distinct().Count() != project.Regions.Count)
            throw new InvalidDataException("Wave contains duplicate annotation identities.");
    }

    private static bool MetadataValid(string name, string? colour, string? category, string? notes, WaveTimelineReference? reference) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 512 && (colour is null || colour.Length <= 64)
        && (category is null || category.Length <= 256) && (notes is null || notes.Length <= 16384)
        && (reference is null || reference.Id != Guid.Empty && !string.IsNullOrWhiteSpace(reference.Owner) && reference.Owner.Length <= 128
            && !string.IsNullOrWhiteSpace(reference.Kind) && reference.Kind.Length <= 128);

    private static WaveProject Commit(WaveProject project, long expectedRevision, List<WaveMarker> markers, List<WaveRegion> regions)
    {
        WaveProjectStore.Validate(project);
        if (project.Revision != expectedRevision) throw new InvalidOperationException("RevisionConflict");
        var updated = project with { Markers = markers.ToList(), Regions = regions.ToList(),
            Revision = checked(project.Revision + 1), ModifiedAt = DateTimeOffset.UtcNow };
        WaveProjectStore.Validate(updated);
        return updated;
    }
}
