using Haven.Core.Games;

namespace Haven.Core.Tests;

/// <summary>APP-GAMES-002: Play/Test is explicit runtime state that cannot silently overwrite authored scenes; applying runtime changes is a
/// separate reviewed operation. Proves identity validation, conflict-on-concurrent-edit (no overwrite), and clean apply preserving other values.</summary>
public sealed class GamesRuntimeApplyBackTests
{
    private static readonly Guid Node = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static GamesAuthoredScene Scene(long revision) => new(revision,
        [new GamesAuthoredValue(Node, "Position.X", 1.0), new GamesAuthoredValue(Other, "Position.X", 5.0)]).Capture();

    private static GamesRuntimeChange Change(Guid id, long baseRevision, double runtimeValue) =>
        new(id, Node, "Position.X", baseRevision, runtimeValue);

    [Fact]
    public void ReviewAgainstMatchingBaseProducesAppliedDiff()
    {
        var scene = Scene(3);
        var diff = GamesRuntimeApplyBack.Review(scene, Change(Guid.NewGuid(), 3, 9.0));
        Assert.Equal(GamesApplyBackOutcome.Applied, diff.Outcome);
        Assert.Equal(1.0, diff.AuthoredValue);
        Assert.Equal(9.0, diff.RuntimeValue);
    }

    [Fact]
    public void ApplyCommitsOnlyTargetAndPreservesOtherValues()
    {
        var scene = Scene(3);
        var applied = GamesRuntimeApplyBack.Apply(scene, Change(Guid.NewGuid(), 3, 9.0));
        Assert.Equal(4, applied.Revision);
        Assert.Equal(9.0, applied.Values.Single(v => v.NodeID == Node).Value);
        // The unrelated authored value is untouched.
        Assert.Equal(5.0, applied.Values.Single(v => v.NodeID == Other).Value);
    }

    [Fact]
    public void ConcurrentAuthoringEditCausesConflictAndNeverOverwrites()
    {
        // Runtime observed against base revision 3; author has since advanced to revision 4.
        var scene = Scene(4);
        var change = Change(Guid.NewGuid(), 3, 9.0);
        var diff = GamesRuntimeApplyBack.Review(scene, change);
        Assert.Equal(GamesApplyBackOutcome.Conflict, diff.Outcome);
        Assert.Equal("GamesApplyBackStaleBase", diff.Reason);

        // Applying a conflicting change throws and leaves the authored scene unchanged.
        Assert.Throws<InvalidOperationException>(() => GamesRuntimeApplyBack.Apply(scene, change));
        Assert.Equal(1.0, scene.Values.Single(v => v.NodeID == Node).Value);
    }

    [Fact]
    public void MissingTargetCausesConflict()
    {
        var scene = Scene(3);
        var change = new GamesRuntimeChange(Guid.NewGuid(), Guid.NewGuid(), "Position.X", 3, 9.0);
        var diff = GamesRuntimeApplyBack.Review(scene, change);
        Assert.Equal(GamesApplyBackOutcome.Conflict, diff.Outcome);
        Assert.Equal("GamesApplyBackTargetMissing", diff.Reason);
    }

    [Fact]
    public void CaptureAloneNeverMutatesAuthoredScene()
    {
        var scene = Scene(3);
        // Capturing a runtime change is observation only; authoring stays at revision 3 with original values.
        _ = Change(Guid.NewGuid(), 3, 42.0).Capture();
        Assert.Equal(3, scene.Revision);
        Assert.Equal(1.0, scene.Values.Single(v => v.NodeID == Node).Value);
    }

    [Fact]
    public void InvalidRuntimeChangeIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => new GamesRuntimeChange(Guid.Empty, Node, "Position.X", 3, 1).Capture());
        Assert.Throws<InvalidDataException>(() => new GamesRuntimeChange(Guid.NewGuid(), Guid.Empty, "Position.X", 3, 1).Capture());
        Assert.Throws<InvalidDataException>(() => new GamesRuntimeChange(Guid.NewGuid(), Node, "Position.X", 0, 1).Capture());
        Assert.Throws<InvalidDataException>(() => new GamesRuntimeChange(Guid.NewGuid(), Node, "Position.X", 3, double.NaN).Capture());
    }
}
