using System.Text.Json;
using System.Text.Json.Serialization;

namespace HavenOS.Apps.Motion;

// FileId is retained as an opaque Files identity; resolution is not available in this Motion-only lane.
internal sealed record MotionAssetReference(Guid AssetId, string FileId);

internal sealed record MotionElement(
    Guid ElementId,
    Guid TrackId,
    Guid AssetId,
    long TimelineStart,
    long Duration,
    long SourceIn,
    long SourceOut,
    Guid? ProxyAssetId = null);

internal sealed record MotionTrack(Guid TrackId, string Name, IReadOnlyList<MotionElement> Elements);

internal sealed record MotionSequence(
    Guid SequenceId,
    int Width,
    int Height,
    int FrameRateNumerator,
    int FrameRateDenominator,
    IReadOnlyList<MotionTrack> VideoTracks);

internal sealed record MotionProject(
    int SchemaVersion,
    Guid ProjectId,
    IReadOnlyList<MotionSequence> Sequences,
    IReadOnlyList<MotionAssetReference> AssetReferences,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    long Revision);

internal sealed class MotionProjectStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public MotionProject Create(string fileId, int width, int height, int frameRateNumerator, int frameRateDenominator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        if (width <= 0 || height <= 0 || frameRateNumerator <= 0 || frameRateDenominator <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Sequence dimensions and frame rate must be positive.");

        var now = DateTimeOffset.UtcNow;
        var assetId = Guid.NewGuid();
        return new MotionProject(CurrentSchemaVersion, Guid.NewGuid(),
            [new MotionSequence(Guid.NewGuid(), width, height, frameRateNumerator, frameRateDenominator,
                [new MotionTrack(Guid.NewGuid(), "Video 1", [])])],
            [new MotionAssetReference(assetId, fileId)], now, now, 0);
    }

    public MotionProject Insert(MotionProject project, long expectedRevision, Guid sequenceId, Guid trackId,
        Guid assetId, long timelineStart, long sourceIn, long sourceOut)
    {
        EnsureRevision(project, expectedRevision);
        if (timelineStart < 0 || sourceIn < 0 || sourceOut <= sourceIn)
            throw new ArgumentOutOfRangeException(nameof(timelineStart), "Timeline and source ranges must be non-negative and have positive duration.");
        if (!project.AssetReferences.Any(asset => asset.AssetId == assetId))
            throw new KeyNotFoundException("AssetNotFound");

        var sequenceIndex = IndexOf(project.Sequences, sequenceId, sequence => sequence.SequenceId, "SequenceNotFound");
        var sequence = project.Sequences[sequenceIndex];
        var trackIndex = IndexOf(sequence.VideoTracks, trackId, track => track.TrackId, "TrackNotFound");
        var track = sequence.VideoTracks[trackIndex];
        var element = new MotionElement(Guid.NewGuid(), trackId, assetId, timelineStart, sourceOut - sourceIn, sourceIn, sourceOut);
        var updatedTrack = track with { Elements = track.Elements.Append(element).OrderBy(item => item.TimelineStart).ThenBy(item => item.ElementId).ToArray() };
        var tracks = sequence.VideoTracks.ToArray();
        tracks[trackIndex] = updatedTrack;
        var sequences = project.Sequences.ToArray();
        sequences[sequenceIndex] = sequence with { VideoTracks = tracks };
        return project with { Sequences = sequences, Revision = checked(project.Revision + 1), ModifiedAt = DateTimeOffset.UtcNow };
    }

    public MotionProject Split(MotionProject project, long expectedRevision, Guid sequenceId, Guid elementId, long timelineTime)
    {
        EnsureRevision(project, expectedRevision);
        var sequenceIndex = IndexOf(project.Sequences, sequenceId, sequence => sequence.SequenceId, "SequenceNotFound");
        var sequence = project.Sequences[sequenceIndex];
        for (var trackIndex = 0; trackIndex < sequence.VideoTracks.Count; trackIndex++)
        {
            var track = sequence.VideoTracks[trackIndex];
            var elementIndex = IndexOfOrDefault(track.Elements, elementId, element => element.ElementId);
            if (elementIndex < 0) continue;
            var original = track.Elements[elementIndex];
            var offset = timelineTime - original.TimelineStart;
            if (offset <= 0 || offset >= original.Duration)
                throw new ArgumentOutOfRangeException(nameof(timelineTime), "Split must be strictly inside the element.");

            var left = original with { Duration = offset, SourceOut = original.SourceIn + offset };
            var right = original with
            {
                ElementId = Guid.NewGuid(), TimelineStart = timelineTime, Duration = original.Duration - offset,
                SourceIn = original.SourceIn + offset
            };
            var elements = track.Elements.ToArray();
            elements[elementIndex] = left;
            var updatedTrack = track with { Elements = elements.Append(right).OrderBy(item => item.TimelineStart).ToArray() };
            var tracks = sequence.VideoTracks.ToArray();
            tracks[trackIndex] = updatedTrack;
            var sequences = project.Sequences.ToArray();
            sequences[sequenceIndex] = sequence with { VideoTracks = tracks };
            return project with { Sequences = sequences, Revision = checked(project.Revision + 1), ModifiedAt = DateTimeOffset.UtcNow };
        }
        throw new KeyNotFoundException("ElementNotFound");
    }

    public void Save(string path, MotionProject project, long expectedStoredRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate(project);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var lockPath = fullPath + ".lock";
        using var projectLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var storedProject = File.Exists(fullPath) ? Load(fullPath) : null;
            var storedRevision = storedProject?.Revision ?? -1;
            if (storedRevision != expectedStoredRevision
                || (storedProject is null && project.Revision != 0)
                || (storedProject is not null
                    && (project.ProjectId != storedProject.ProjectId
                        || project.SchemaVersion != storedProject.SchemaVersion
                        || project.Revision != checked(expectedStoredRevision + 1))))
                throw new InvalidOperationException("RevisionConflict");
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, project, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public MotionProject Load(string path)
    {
        using var stream = File.OpenRead(path);
        var project = JsonSerializer.Deserialize<MotionProject>(stream, JsonOptions)
            ?? throw new InvalidDataException("Project document is empty.");
        if (project.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported Motion project schema version {project.SchemaVersion}.");
        Validate(project);
        return project;
    }

    private static void Validate(MotionProject project)
    {
        if (project.Sequences is null || project.AssetReferences is null
            || project.SchemaVersion != CurrentSchemaVersion || project.ProjectId == Guid.Empty || project.Revision < 0
            || project.Sequences.Count == 0 || project.Sequences.Any(sequence => sequence is null || sequence.SequenceId == Guid.Empty
                || sequence.VideoTracks is null || sequence.Width <= 0 || sequence.Height <= 0
                || sequence.FrameRateNumerator <= 0 || sequence.FrameRateDenominator <= 0))
            throw new InvalidDataException("Motion project identity, schema, revision, or sequence metadata is invalid.");
        if (project.AssetReferences.Any(asset => asset is null)) throw new InvalidDataException("Motion asset references cannot be null.");
        var assets = project.AssetReferences.Select(asset => asset.AssetId).ToHashSet();
        if (assets.Count != project.AssetReferences.Count || project.AssetReferences.Any(asset => asset.AssetId == Guid.Empty || string.IsNullOrWhiteSpace(asset.FileId)))
            throw new InvalidDataException("Motion asset references must have unique stable IDs and a canonical FileID.");
        var sequenceIds = new HashSet<Guid>();
        var trackIds = new HashSet<Guid>();
        var elementIds = new HashSet<Guid>();
        foreach (var sequence in project.Sequences)
        {
            if (!sequenceIds.Add(sequence.SequenceId)) throw new InvalidDataException("Sequence IDs must be unique.");
            foreach (var track in sequence.VideoTracks)
            {
                if (track is null || track.Elements is null || !trackIds.Add(track.TrackId)) throw new InvalidDataException("Track IDs and element lists must be valid and unique.");
                foreach (var element in track.Elements)
                    if (element is null || element.TrackId != track.TrackId || !elementIds.Add(element.ElementId) || !assets.Contains(element.AssetId)
                        || element.TimelineStart < 0 || element.Duration <= 0 || element.SourceIn < 0 || element.SourceOut - element.SourceIn != element.Duration)
                        throw new InvalidDataException("Motion element identity, asset, or time ranges are invalid.");
            }
        }
    }

    private static void EnsureRevision(MotionProject project, long expectedRevision)
    {
        if (project.Revision != expectedRevision) throw new InvalidOperationException("RevisionConflict");
    }

    private static int IndexOf<T>(IReadOnlyList<T> values, Guid id, Func<T, Guid> getId, string error)
    {
        var index = IndexOfOrDefault(values, id, getId);
        return index >= 0 ? index : throw new KeyNotFoundException(error);
    }

    private static int IndexOfOrDefault<T>(IReadOnlyList<T> values, Guid id, Func<T, Guid> getId)
    {
        for (var index = 0; index < values.Count; index++) if (getId(values[index]) == id) return index;
        return -1;
    }
}
