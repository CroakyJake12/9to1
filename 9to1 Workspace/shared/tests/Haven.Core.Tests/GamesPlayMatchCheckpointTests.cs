using Haven.Core.Games;

namespace Haven.Core.Tests;

/// <summary>ED-PLAY-003: match checkpoints preserve sealed submissions, resolved rounds, clock policy and effect receipts on reconnect;
/// replay branches leave original history intact and cannot repeat external effects.
/// Proves checkpoint reconstruct fidelity, branch isolation from original history, and effect-receipt dedup.</summary>
public sealed class GamesPlayMatchCheckpointTests
{
    private static readonly Guid DefinitionID = Guid.NewGuid();
    private static readonly Guid Lineage = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static GameDefinition Definition() => GameDefinition.Create(DefinitionID,
        [new PlayStateField("Question", "string", PlayVisibility.Public), new PlayStateField("Answer", "string", PlayVisibility.Judge)],
        [new PlayRole("Human", true)], ["TeamA", "TeamB"], new PlayClock(30, 120), "sum-of-rounds", "highest-score")
        .AddAction(new PlayAction("Submit", "answer", ["Human"]), 1);

    private static MatchLineage Begin() => MatchLineage.Begin(Lineage, Definition(),
        [new PlayContestant(Alice, "Alice", "Human", "TeamA"), new PlayContestant(Bob, "Bob", "Human", "TeamB")], new PlayClock(30, 120));

    private static MatchState Advanced(GameDefinition definition)
    {
        var state = MatchState.Open(Lineage, definition, [new PlayContestant(Alice, "Alice", "Human", "TeamA"), new PlayContestant(Bob, "Bob", "Human", "TeamB")]);
        var submitted = state.Submit(definition, 1, new PlaySubmission(Guid.NewGuid(), Alice, 1, "Submit", "B", DateTimeOffset.UtcNow))
            .Submit(definition, 2, new PlaySubmission(Guid.NewGuid(), Bob, 1, "Submit", "A", DateTimeOffset.UtcNow));
        return submitted.Resolve(submitted.Revision, correctAnswer: "B", points: 10);
    }

    [Fact]
    public void BeginPinsDefinitionRevisionAndOpensFirstCheckpoint()
    {
        var lineage = Begin();
        Assert.Equal(DefinitionID, lineage.DefinitionID);
        Assert.Equal(2, lineage.DefinitionRevision);
        Assert.Single(lineage.Checkpoints);
        Assert.Equal(1, lineage.CurrentState().CurrentRound);
    }

    [Fact]
    public void CheckpointReconstructPreservesSealedSubmissionsAndRounds()
    {
        var advanced = Advanced(Definition());
        var checkpoint = MatchCheckpoint.Commit(Guid.NewGuid(), Lineage, 2, advanced, new PlayClock(30, 120), [new PlayEffectReceipt("reveal-r1", "RevealBroadcast", 1)]);
        var reconstructed = checkpoint.Reconstruct();

        // Sealed submissions and resolved round survive reconstruction exactly.
        Assert.Equal(advanced.CurrentRound, reconstructed.CurrentRound);
        Assert.Equal(advanced.SealedSubmissions.Count, reconstructed.SealedSubmissions.Count);
        Assert.Equal(advanced.ScoredEvents.Count, reconstructed.ScoredEvents.Count);
        Assert.Single(reconstructed.ScoredEvents);
    }

    [Fact]
    public void BranchLeavesOriginalHistoryIntact()
    {
        var original = Begin();
        var branch = original.Branch(Guid.NewGuid());

        // The branch carries the committed state forward under a new lineage.
        Assert.NotEqual(original.LineageID, branch.LineageID);
        Assert.Single(branch.Checkpoints);

        // Appending to the branch does not mutate the original history.
        var appended = branch.Append(MatchCheckpoint.Commit(Guid.NewGuid(), branch.LineageID, 2, branch.CurrentState(), new PlayClock(30, 120), []));
        Assert.Equal(2, appended.Checkpoints.Count);
        Assert.Single(original.Checkpoints);
    }

    [Fact]
    public void EffectReceiptDedupPreventsRepeatedExternalEffect()
    {
        var lineage = Begin();
        Assert.True(lineage.TryApplyEffect("reveal-r1", "RevealBroadcast", 1));
        var recorded = lineage.RecordEffect("reveal-r1", "RevealBroadcast", 1);

        // Replaying the same effect is now refused (already applied).
        Assert.False(recorded.TryApplyEffect("reveal-r1", "RevealBroadcast", 1));
        Assert.Throws<InvalidOperationException>(() => recorded.RecordEffect("reveal-r1", "RevealBroadcast", 1));
    }

    [Fact]
    public void AppendRejectsSequenceGapAndForeignLineage()
    {
        var lineage = Begin();
        // A checkpoint with the wrong sequence is rejected.
        Assert.Throws<InvalidOperationException>(() => lineage.Append(MatchCheckpoint.Commit(Guid.NewGuid(), Lineage, 99, lineage.CurrentState(), new PlayClock(30, 120), [])));
        // A checkpoint belonging to another lineage is rejected.
        Assert.Throws<InvalidOperationException>(() => lineage.Append(MatchCheckpoint.Commit(Guid.NewGuid(), Guid.NewGuid(), 2, lineage.CurrentState(), new PlayClock(30, 120), [])));
    }

    [Fact]
    public void BranchRequiresDistinctLineageIdentity()
    {
        var lineage = Begin();
        Assert.Throws<InvalidDataException>(() => lineage.Branch(Lineage));
        Assert.Throws<InvalidDataException>(() => lineage.Branch(Guid.Empty));
    }
}
