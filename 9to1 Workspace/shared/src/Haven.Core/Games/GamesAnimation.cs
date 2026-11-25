namespace Haven.Core.Games;

/// <summary>Supported authored interpolation modes. Unsupported values are rejected rather than approximated.</summary>
public enum GamesAnimationInterpolation { Constant, Linear }

/// <summary>The kind of value a track animates. A key cannot be retargeted to an incompatible kind.
/// Vector3 and Quaternion use three or four components; Scalar uses one.</summary>
public enum GamesAnimationValueKind { Scalar, Vector3, Quaternion }

/// <summary>A stable authored animation asset identity. It is independent of any scene instance so the same asset can be referenced by multiple instances without copying the scene.</summary>
public sealed record GamesAnimationAsset(Guid AnimationID, string Name, double DurationSeconds)
{
    public const int MaximumTracks = 256;
    public const int MaximumKeysPerTrack = 4096;

    public GamesAnimationAsset Capture()
    {
        if (AnimationID == Guid.Empty) throw new InvalidDataException("Games animation identity is missing.");
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Games animation name is required.");
        if (!double.IsFinite(DurationSeconds) || DurationSeconds < 0) throw new InvalidDataException("Games animation duration must be finite and non-negative.");
        return this;
    }
}

/// <summary>A single authored key. Time is absolute within the clip; Value length must match the owning track's value kind.</summary>
public sealed record GamesAnimationKey(Guid KeyID, double TimeSeconds, IReadOnlyList<double> Value, GamesAnimationInterpolation Interpolation)
{
    public GamesAnimationKey Capture()
    {
        if (KeyID == Guid.Empty) throw new InvalidDataException("Games animation key identity is missing.");
        if (!double.IsFinite(TimeSeconds) || TimeSeconds < 0) throw new InvalidDataException("Games animation key time must be finite and non-negative.");
        if (Value is null || Value.Count is < 1 or > 4) throw new InvalidDataException("Games animation key value must contain one to four components.");
        if (Value.Any(component => !double.IsFinite(component))) throw new InvalidDataException("Games animation key values must be finite.");
        if (!Enum.IsDefined(Interpolation)) throw new InvalidDataException("Games animation interpolation is unsupported.");
        return this with { Value = Value.ToArray() };
    }
}

/// <summary>A property track bound to a specific target object and property. The binding is stable: editing or moving keys never retargets it.</summary>
public sealed record GamesAnimationTrack(Guid TrackID, Guid TargetObjectID, string PropertyName,
    GamesAnimationValueKind ValueKind, IReadOnlyList<GamesAnimationKey> Keys)
{
    public GamesAnimationTrack Capture()
    {
        if (TrackID == Guid.Empty) throw new InvalidDataException("Games animation track identity is missing.");
        if (TargetObjectID == Guid.Empty) throw new InvalidDataException("Games animation track target object is missing.");
        if (string.IsNullOrWhiteSpace(PropertyName)) throw new InvalidDataException("Games animation track property name is required.");
        if (!Enum.IsDefined(ValueKind)) throw new InvalidDataException("Games animation track value kind is unsupported.");
        if (Keys is null) throw new InvalidDataException("Games animation track keys are missing.");
        var snapshot = Keys.Select(key => key?.Capture() ?? throw new InvalidDataException("Games animation contains a missing key.")).ToArray();
        int expected = ValueKind switch { GamesAnimationValueKind.Scalar => 1, GamesAnimationValueKind.Vector3 => 3, GamesAnimationValueKind.Quaternion => 4, _ => throw new InvalidDataException("Games animation track value kind is unsupported.") };
        if (snapshot.Any(key => key.Value.Count != expected)) throw new InvalidDataException("Games animation key component count does not match the track value kind.");
        if (snapshot.Select(key => key.KeyID).Distinct().Count() != snapshot.Length) throw new InvalidDataException("Games animation contains duplicate key identities.");
        return this with { Keys = Array.AsReadOnly(snapshot) };
    }
}

/// <summary>An authored animation document: an asset plus its tracks. Revision-aware edits commit transactions; the original is never mutated.</summary>
public sealed record GamesAnimationDocument(GamesAnimationAsset Asset, int Revision, IReadOnlyList<GamesAnimationTrack> Tracks)
{
    public const int CurrentSchemaVersion = 1;

    public GamesAnimationDocument Capture()
    {
        var asset = Asset.Capture();
        if (Revision < 1) throw new InvalidDataException("Games animation revision must be positive.");
        if (Tracks is null) throw new InvalidDataException("Games animation tracks are missing.");
        if (Tracks.Count > GamesAnimationAsset.MaximumTracks) throw new InvalidDataException("Games animation exceeds the configured track limit.");
        var snapshot = Tracks.Select(track => track?.Capture() ?? throw new InvalidDataException("Games animation contains a missing track.")).ToArray();
        if (snapshot.Select(track => track.TrackID).Distinct().Count() != snapshot.Length) throw new InvalidDataException("Games animation contains duplicate track identities.");
        if (snapshot.Any(track => track.Keys.Count > GamesAnimationAsset.MaximumKeysPerTrack)) throw new InvalidDataException("Games animation exceeds the configured key limit.");
        return this with { Asset = asset, Tracks = Array.AsReadOnly(snapshot) };
    }

    public static GamesAnimationDocument Create(GamesAnimationAsset asset)
        => new(asset.Capture(), 1, Array.Empty<GamesAnimationTrack>());

    private GamesAnimationDocument Next(int expectedRevision, IReadOnlyList<GamesAnimationTrack> tracks)
    {
        if (Revision != expectedRevision) throw new InvalidOperationException("GamesAnimationRevisionConflict");
        return new GamesAnimationDocument(Asset, checked(Revision + 1), tracks).Capture();
    }

    public GamesAnimationDocument AddTrack(GamesAnimationTrack track, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (Tracks.Any(existing => existing.TrackID == track.TrackID)) throw new InvalidOperationException("GamesAnimationTrackAlreadyPresent");
        return Next(expectedRevision, Tracks.Append(track).ToArray());
    }

    public GamesAnimationDocument RemoveTrack(Guid trackID, int expectedRevision)
    {
        var current = Tracks.SingleOrDefault(track => track.TrackID == trackID) ?? throw new KeyNotFoundException("GamesAnimationTrackNotFound");
        _ = current;
        return Next(expectedRevision, Tracks.Where(track => track.TrackID != trackID).ToArray());
    }

    public GamesAnimationDocument AddKey(Guid trackID, GamesAnimationKey key, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(key);
        var tracks = Tracks.Select(track =>
        {
            if (track.TrackID != trackID) return track;
            if (track.Keys.Any(existing => existing.KeyID == key.KeyID)) throw new InvalidOperationException("GamesAnimationKeyAlreadyPresent");
            return track with { Keys = track.Keys.Append(key).ToArray() };
        }).ToArray();
        if (!Tracks.Any(track => track.TrackID == trackID)) throw new KeyNotFoundException("GamesAnimationTrackNotFound");
        return Next(expectedRevision, tracks);
    }

    public GamesAnimationDocument UpdateKey(Guid trackID, Guid keyID, double? timeSeconds, IReadOnlyList<double>? value,
        GamesAnimationInterpolation? interpolation, int expectedRevision)
    {
        var tracks = Tracks.Select(track =>
        {
            if (track.TrackID != trackID) return track;
            var keys = track.Keys.Select(key => key.KeyID == keyID
                ? key with { TimeSeconds = timeSeconds ?? key.TimeSeconds, Value = value ?? key.Value, Interpolation = interpolation ?? key.Interpolation }
                : key).ToArray();
            if (!keys.Any(key => key.KeyID == keyID)) throw new KeyNotFoundException("GamesAnimationKeyNotFound");
            return track with { Keys = keys };
        }).ToArray();
        if (!Tracks.Any(track => track.TrackID == trackID)) throw new KeyNotFoundException("GamesAnimationTrackNotFound");
        return Next(expectedRevision, tracks);
    }

    public GamesAnimationDocument MoveKeys(Guid trackID, IReadOnlyList<Guid> keyIDs, double deltaSeconds, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(keyIDs);
        if (!double.IsFinite(deltaSeconds)) throw new InvalidDataException("Games animation move delta must be finite.");
        var selected = keyIDs.ToHashSet();
        if (selected.Count == 0) return this;

        double minTime = Tracks.Single(track => track.TrackID == trackID).Keys
            .Where(key => selected.Contains(key.KeyID))
            .Select(key => key.TimeSeconds)
            .DefaultIfEmpty(0.0)
            .Min();

        // Enforce coherent group movement: if the delta would push any selected key below zero, clamp the entire group by the same amount to preserve relative spacing.
        double effectiveDelta = (minTime + deltaSeconds < 0) ? -minTime : deltaSeconds;

        var tracks = Tracks.Select(track =>
        {
            if (track.TrackID != trackID) return track;
            var keys = track.Keys.Select(key => selected.Contains(key.KeyID)
                ? key with { TimeSeconds = key.TimeSeconds + effectiveDelta } : key).ToArray();
            return track with { Keys = keys };
        }).ToArray();
        if (!Tracks.Any(track => track.TrackID == trackID)) throw new KeyNotFoundException("GamesAnimationTrackNotFound");
        return Next(expectedRevision, tracks);
    }

    public GamesAnimationDocument DeleteKey(Guid trackID, Guid keyID, int expectedRevision)
    {
        var tracks = Tracks.Select(track => track.TrackID != trackID ? track : track with { Keys = track.Keys.Where(key => key.KeyID != keyID).ToArray() }).ToArray();
        if (!Tracks.Any(track => track.TrackID == trackID)) throw new KeyNotFoundException("GamesAnimationTrackNotFound");
        return Next(expectedRevision, tracks);
    }

    public GamesAnimationDocument SetDuration(double durationSeconds, int expectedRevision)
    {
        var asset = Asset with { DurationSeconds = durationSeconds };
        if (Revision != expectedRevision) throw new InvalidOperationException("GamesAnimationRevisionConflict");
        return new GamesAnimationDocument(asset.Capture(), checked(Revision + 1), Tracks).Capture();
    }

    /// <summary>Keys that fall beyond a shortened clip duration. Callers must review these explicitly before committing the shorten.</summary>
    public IReadOnlyList<GamesAnimationKey> KeysBeyondDuration(double durationSeconds)
        => Tracks.SelectMany(track => track.Keys).Where(key => key.TimeSeconds > durationSeconds).ToArray();
}
