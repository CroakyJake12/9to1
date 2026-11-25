namespace Haven.Core.Games;

/// <summary>The lifecycle of a disposable Play/Test runtime session. Authoring source, saved project and runtime state have separate revisions.
/// Stop discards transient changes unless explicitly applied.</summary>
public enum GamesRuntimeSessionPhase { Running, Paused, Stopped }

/// <summary>A transient runtime transform override for one node/property. It lives only in the session's runtime layer and never touches authoring.
/// The base authored revision is pinned at capture so apply-back can detect a concurrent authoring edit.</summary>
public sealed record GamesRuntimeTransform(Guid NodeID, string PropertyName, double BaseAuthoringRevision, double RuntimeValue)
{
    public GamesRuntimeTransform Capture()
    {
        if (NodeID == Guid.Empty) throw new InvalidDataException("Games runtime transform node is missing.");
        if (string.IsNullOrWhiteSpace(PropertyName)) throw new InvalidDataException("Games runtime transform property name is required.");
        if (BaseAuthoringRevision < 1) throw new InvalidDataException("Games runtime transform base revision must be positive.");
        if (!double.IsFinite(RuntimeValue)) throw new InvalidDataException("Games runtime transform value must be finite.");
        return this;
    }
}

/// <summary>A distinct Play/Test session that snapshots the authored scene at a pinned revision and holds a separate runtime transform layer.
/// Moving objects at runtime writes only to that layer; the authored snapshot is never mutated. Apply-back is a separate reviewed action.</summary>
public sealed record GamesRuntimeSession(Guid SessionID, Guid SceneID, long AuthoredBaseRevision, GamesRuntimeSessionPhase Phase,
    GamesAuthoredScene AuthoredSnapshot, IReadOnlyList<GamesRuntimeTransform> RuntimeLayer)
{
    public const int CurrentSchemaVersion = 1;

    public GamesRuntimeSession Capture()
    {
        if (SessionID == Guid.Empty) throw new InvalidDataException("Games runtime session identity is missing.");
        if (SceneID == Guid.Empty) throw new InvalidDataException("Games runtime session scene is missing.");
        if (AuthoredBaseRevision < 1) throw new InvalidDataException("Games runtime session base revision must be positive.");
        if (!Enum.IsDefined(Phase)) throw new InvalidDataException("Games runtime session phase is unsupported.");
        var snapshot = AuthoredSnapshot.Capture();
        if (snapshot.Revision != AuthoredBaseRevision) throw new InvalidOperationException("GamesRuntimeSessionBaseMismatch");
        var layer = (RuntimeLayer ?? Array.Empty<GamesRuntimeTransform>()).Select(t => t?.Capture() ?? throw new InvalidDataException("A games runtime transform is missing.")).ToArray();
        if (layer.Select(t => (t.NodeID, t.PropertyName)).Distinct().Count() != layer.Length) throw new InvalidOperationException("GamesRuntimeDuplicateTransform");
        return this with { AuthoredSnapshot = snapshot, RuntimeLayer = layer };
    }

    /// <summary>Start a session by snapshotting the authored scene at its current revision. The snapshot is immutable for the session's life.</summary>
    public static GamesRuntimeSession Start(Guid sessionID, Guid sceneID, GamesAuthoredScene authored)
    {
        var captured = authored.Capture();
        return new GamesRuntimeSession(sessionID, sceneID, captured.Revision, GamesRuntimeSessionPhase.Running, captured, Array.Empty<GamesRuntimeTransform>()).Capture();
    }

    /// <summary>Move an object at runtime. This writes only to the session's runtime layer; the authored snapshot is untouched.</summary>
    public GamesRuntimeSession MoveAtRuntime(Guid nodeID, string propertyName, double value)
    {
        if (Phase != GamesRuntimeSessionPhase.Running) throw new InvalidOperationException("GamesRuntimeSessionNotRunning");
        var transform = new GamesRuntimeTransform(nodeID, propertyName, AuthoredBaseRevision, value).Capture();
        // Only nodes that exist in the authored snapshot may be moved at runtime.
        if (!AuthoredSnapshot.Values.Any(v => v.NodeID == nodeID && v.PropertyName == propertyName)) throw new InvalidOperationException("GamesRuntimeTargetNotInScene");
        var layer = RuntimeLayer.Where(t => !(t.NodeID == nodeID && t.PropertyName == propertyName)).Append(transform).ToArray();
        return this with { RuntimeLayer = layer }.Capture();
    }

    public GamesRuntimeSession Pause() => Phase == GamesRuntimeSessionPhase.Running ? this with { Phase = GamesRuntimeSessionPhase.Paused } : throw new InvalidOperationException("GamesRuntimeSessionNotRunning");
    public GamesRuntimeSession Resume() => Phase == GamesRuntimeSessionPhase.Paused ? this with { Phase = GamesRuntimeSessionPhase.Running } : throw new InvalidOperationException("GamesRuntimeSessionNotPaused");

    /// <summary>Stop ends the session and returns to authoring state. Unapplied runtime changes are discarded; the authored snapshot is unchanged.</summary>
    public GamesRuntimeSession Stop() => this with { Phase = GamesRuntimeSessionPhase.Stopped, RuntimeLayer = Array.Empty<GamesRuntimeTransform>() }.Capture();

    /// <summary>The effective value for a node/property: the runtime override when present, otherwise the authored value. Read-only view.</summary>
    public double EffectiveValue(Guid nodeID, string propertyName)
        => RuntimeLayer.FirstOrDefault(t => t.NodeID == nodeID && t.PropertyName == propertyName)?.RuntimeValue
            ?? AuthoredSnapshot.Values.Single(v => v.NodeID == nodeID && v.PropertyName == propertyName).Value;

    /// <summary>Build a reviewed apply-back candidate for one runtime transform, pinned to the base authored revision it was observed against.</summary>
    public GamesRuntimeChange ToApplyBackCandidate(Guid changeID, Guid nodeID, string propertyName)
        => new(changeID, nodeID, propertyName, AuthoredBaseRevision, RuntimeLayer.Single(t => t.NodeID == nodeID && t.PropertyName == propertyName).RuntimeValue);

        /// <summary>Apply a reviewed runtime transform to the current authored scene. Delegates to GamesRuntimeApplyBack so a concurrent authoring edit
    /// (scene advanced past the base revision) yields conflict resolution instead of overwriting.</summary>
    public GamesAuthoredScene ApplyBack(GamesAuthoredScene currentAuthored, Guid nodeID, string propertyName)
    {
        var candidate = ToApplyBackCandidate(Guid.NewGuid(), nodeID, propertyName);
        return GamesRuntimeApplyBack.Apply(currentAuthored, candidate);
    }
}
