using Haven.Core.Games;

namespace Haven.Core.Tests;

/// <summary>IX-GAMES-003: moving an object only at runtime, then stopping and reopening the scene, leaves the authored transform unchanged.
/// Applying a reviewed runtime transform after a concurrent authoring edit requires conflict resolution instead of overwriting.</summary>
public sealed class GamesPlayRuntimeSessionTests
{
    private static readonly Guid Scene = Guid.NewGuid();
    private static readonly Guid Session = Guid.NewGuid();
    private static readonly Guid Node = Guid.NewGuid();

    private static GamesAuthoredScene Authored(long revision) =>
        new GamesAuthoredScene(revision, [new GamesAuthoredValue(Node, "Position.X", 1.0)]).Capture();

    [Fact]
    public void StartSnapshotsAuthoredRevision()
    {
        var session = GamesRuntimeSession.Start(Session, Scene, Authored(3));
        Assert.Equal(3, session.AuthoredBaseRevision);
        Assert.Equal(GamesRuntimeSessionPhase.Running, session.Phase);
        Assert.Empty(session.RuntimeLayer);
    }

    [Fact]
    public void MoveAtRuntimeLeavesAuthoredSnapshotUnchanged()
    {
        var authored = Authored(3);
        var session = GamesRuntimeSession.Start(Session, Scene, authored).MoveAtRuntime(Node, "Position.X", 9.0);

        // The runtime layer holds the move; the authored snapshot is untouched.
        Assert.Equal(9.0, session.EffectiveValue(Node, "Position.X"));
        Assert.Equal(1.0, session.AuthoredSnapshot.Values.Single(v => v.NodeID == Node).Value);
    }

    [Fact]
    public void StopDiscardsUnappliedRuntimeStateAndReopenKeepsAuthoredTransform()
    {
        var authored = Authored(3);
        var moved = GamesRuntimeSession.Start(Session, Scene, authored).MoveAtRuntime(Node, "Position.X", 9.0);
        var stopped = moved.Stop();

        // Stop discards transient runtime changes; the authored transform stays unchanged.
        Assert.Equal(GamesRuntimeSessionPhase.Stopped, stopped.Phase);
        Assert.Empty(stopped.RuntimeLayer);
        Assert.Equal(1.0, stopped.AuthoredSnapshot.Values.Single(v => v.NodeID == Node).Value);

        // Reopening a fresh session over the same authored scene still shows the original transform.
        var reopened = GamesRuntimeSession.Start(Guid.NewGuid(), Scene, stopped.AuthoredSnapshot);
        Assert.Equal(1.0, reopened.EffectiveValue(Node, "Position.X"));
    }

    [Fact]
    public void ApplyBackCleanWhenNoConcurrentEdit()
    {
        var authored = Authored(3);
        var session = GamesRuntimeSession.Start(Session, Scene, authored).MoveAtRuntime(Node, "Position.X", 9.0);

        // No concurrent authoring edit: apply-back commits the reviewed runtime transform.
        var applied = session.ApplyBack(authored, Node, "Position.X");
        Assert.Equal(4, applied.Revision);
        Assert.Equal(9.0, applied.Values.Single(v => v.NodeID == Node).Value);
    }

    [Fact]
    public void ApplyBackAfterConcurrentAuthoringEditRequiresConflictResolution()
    {
        var authored = Authored(3);
        var session = GamesRuntimeSession.Start(Session, Scene, authored).MoveAtRuntime(Node, "Position.X", 9.0);

        // A concurrent authoring edit advanced the scene past the session's base revision.
        var concurrentlyEdited = authored.WithValue(Node, "Position.X", 2.5) with { Revision = 4 };
        Assert.Throws<InvalidOperationException>(() => session.ApplyBack(concurrentlyEdited, Node, "Position.X"));

        // The concurrent authoring value is preserved; the runtime move did not overwrite it.
        Assert.Equal(2.5, concurrentlyEdited.Values.Single(v => v.NodeID == Node).Value);
    }

    [Fact]
    public void MoveRejectsUnknownTargetAndNonRunningPhase()
    {
        var session = GamesRuntimeSession.Start(Session, Scene, Authored(3));
        Assert.Throws<InvalidOperationException>(() => session.MoveAtRuntime(Guid.NewGuid(), "Position.X", 1.0));

        var paused = session.Pause();
        Assert.Throws<InvalidOperationException>(() => paused.MoveAtRuntime(Node, "Position.X", 1.0));
    }

    [Fact]
    public void PauseResumeCycleIsEnforced()
    {
        var session = GamesRuntimeSession.Start(Session, Scene, Authored(3));
        Assert.Equal(GamesRuntimeSessionPhase.Paused, session.Pause().Phase);
        Assert.Equal(GamesRuntimeSessionPhase.Running, session.Pause().Resume().Phase);
        // Resuming a running session is invalid.
        Assert.Throws<InvalidOperationException>(() => session.Resume());
    }
}
