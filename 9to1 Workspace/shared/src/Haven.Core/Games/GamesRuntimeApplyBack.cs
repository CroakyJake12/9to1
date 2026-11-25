namespace Haven.Core.Games;

/// <summary>The outcome of reviewing a captured runtime change for apply-back to the authored scene.
/// Applied commits the reviewed value; Conflict refuses to overwrite a concurrently edited target; Rejected discards it.</summary>
public enum GamesApplyBackOutcome { Applied, Conflict, Rejected }

/// <summary>A single authored property value keyed by node identity and property name. This is the minimal shared representation used to model
/// an authored scene's mutable properties without depending on the full scene snapshot owned elsewhere.</summary>
public sealed record GamesAuthoredValue(Guid NodeID, string PropertyName, double Value);

/// <summary>The authored scene state captured at a specific revision. Apply-back validates against this so runtime changes never silently clobber authoring.</summary>
public sealed record GamesAuthoredScene(long Revision, IReadOnlyList<GamesAuthoredValue> Values)
{
    public GamesAuthoredScene Capture()
    {
        if (Revision < 1) throw new InvalidDataException("Games authored scene revision must be positive.");
        if (Values is null) throw new InvalidDataException("Games authored scene values are missing.");
        var snapshot = Values.ToArray();
        if (snapshot.Select(v => (v.NodeID, v.PropertyName)).Distinct().Count() != snapshot.Length)
            throw new InvalidOperationException("GamesAuthoredDuplicateValue");
        return this with { Values = snapshot };
    }

    public GamesAuthoredScene WithValue(Guid nodeID, string propertyName, double value) =>
        (new GamesAuthoredScene(Revision, Values.Where(v => !(v.NodeID == nodeID && v.PropertyName == propertyName)).Append(new GamesAuthoredValue(nodeID, propertyName, value)).ToArray())).Capture();
}

/// <summary>A runtime change captured during Play/Test. It pins the exact target identity (node + property), the authored base revision it was
/// observed against, and the sampled runtime value. Applying is a separate reviewed operation; capture alone never mutates authoring.</summary>
public sealed record GamesRuntimeChange(Guid ChangeID, Guid NodeID, string PropertyName, long BaseAuthoringRevision, double RuntimeValue)
{
    public GamesRuntimeChange Capture()
    {
        if (ChangeID == Guid.Empty) throw new InvalidDataException("Games runtime change identity is missing.");
        if (NodeID == Guid.Empty) throw new InvalidDataException("Games runtime change target node is missing.");
        if (string.IsNullOrWhiteSpace(PropertyName)) throw new InvalidDataException("Games runtime change property name is required.");
        if (BaseAuthoringRevision < 1) throw new InvalidDataException("Games runtime change base revision must be positive.");
        if (!double.IsFinite(RuntimeValue)) throw new InvalidDataException("Games runtime change value must be finite.");
        return this;
    }
}

/// <summary>The explicit diff produced when reviewing a runtime change against the current authored scene.</summary>
public sealed record GamesApplyBackDiff(Guid ChangeID, GamesApplyBackOutcome Outcome, double? AuthoredValue, double RuntimeValue, string? Reason);

/// <summary>Pure apply-back review logic. It never mutates the authored scene unless a change is explicitly Applied, and it refuses to overwrite
/// a target whose authored value changed since the runtime observation (a concurrent authoring edit) rather than silently clobbering it.</summary>
public static class GamesRuntimeApplyBack
{
    /// <summary>Review one captured runtime change against the current authored scene. Produces an explicit diff and outcome without mutating anything.</summary>
    public static GamesApplyBackDiff Review(GamesAuthoredScene scene, GamesRuntimeChange change)
    {
        var captured = scene.Capture();
        change.Capture();

        // The target must still exist in the authored scene.
        if (!captured.Values.Any(v => v.NodeID == change.NodeID && v.PropertyName == change.PropertyName))
            return new GamesApplyBackDiff(change.ChangeID, GamesApplyBackOutcome.Conflict, null, change.RuntimeValue, "GamesApplyBackTargetMissing");

        // The authored base revision must still match; a newer authoring edit means the observation is stale.
        if (captured.Revision != change.BaseAuthoringRevision)
            return new GamesApplyBackDiff(change.ChangeID, GamesApplyBackOutcome.Conflict,
                captured.Values.Single(v => v.NodeID == change.NodeID && v.PropertyName == change.PropertyName).Value,
                change.RuntimeValue, "GamesApplyBackStaleBase");

        var current = captured.Values.Single(v => v.NodeID == change.NodeID && v.PropertyName == change.PropertyName);
        return new GamesApplyBackDiff(change.ChangeID, GamesApplyBackOutcome.Applied, current.Value, change.RuntimeValue, null);
    }

    /// <summary>Apply a reviewed runtime change to the authored scene. Only an Applied outcome commits; Conflict/Rejected leave authoring unchanged.
/// The returned scene carries the next revision. Other authored values are preserved.</summary>
    public static GamesAuthoredScene Apply(GamesAuthoredScene scene, GamesRuntimeChange change)
    {
        var diff = Review(scene, change);
        if (diff.Outcome != GamesApplyBackOutcome.Applied) throw new InvalidOperationException(diff.Reason ?? "GamesApplyBackRejected");
        return (scene.WithValue(change.NodeID, change.PropertyName, change.RuntimeValue)) with { Revision = checked(scene.Revision + 1) };
    }
}
