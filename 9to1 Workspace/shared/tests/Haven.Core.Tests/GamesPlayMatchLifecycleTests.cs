using Haven.Core.Games;

namespace Haven.Core.Tests;

/// <summary>IX-PLAY-002: opening Match rules in a no-tools round and attempting a participant tool call must be denied by runtime capability,
/// while replaying the completed match leaves scores and submitted answers unchanged.</summary>
public sealed class GamesPlayMatchLifecycleTests
{
    private static readonly Guid DefinitionID = Guid.NewGuid();
    private static readonly Guid Match = Guid.NewGuid();

    private static GameDefinition Definition() => GameDefinition.Create(DefinitionID,
        [new PlayStateField("Question", "string", PlayVisibility.Public)], [new PlayRole("Human", true)], ["TeamA"], new PlayClock(30, 120), "sum", "high");

    private static PlayMatch OpenNoTools() => PlayMatch.Open(Match, Definition(),
        [PlayMatchRound.Open(1, PlayToolProfile.NoTools("strict-r1", "Strict Round 1"))]);

    [Fact]
    public void NoToolsRoundDeniesParticipantToolCallByRuntimeCapability()
    {
        var match = OpenNoTools();
        var round = match.Rounds.Single();
        var result = round.AttemptToolCall("web.search");

        Assert.False(result.Allowed);
        // Denial is enforced by runtime capability, not merely a prompt instruction.
        Assert.True(result.DeniedByRuntimeCapability);
        Assert.Equal("PlayNoOutsideAssistance", result.Authorization.Reason);
    }

    [Fact]
    public void AssistedRoundAllowsDeclaredTool()
    {
        var round = PlayMatchRound.Open(1, PlayToolProfile.WithTools("assisted-r1", "Assisted Round 1", ["calculator"]));
        Assert.True(round.AttemptToolCall("calculator").Allowed);
        // An undeclared tool is denied but not by the no-tools capability gate.
        var denied = round.AttemptToolCall("web.search");
        Assert.False(denied.Allowed);
        Assert.False(denied.DeniedByRuntimeCapability);
    }

    [Fact]
    public void PauseFreezesAndResumeContinuesSameMatch()
    {
        var match = OpenNoTools();
        var paused = match.Pause(match.Revision);
        Assert.Equal(PlayMatchPhase.Paused, paused.Phase);

        // Pausing twice is invalid; resuming continues the same pinned match.
        Assert.Throws<InvalidOperationException>(() => paused.Pause(paused.Revision));
        var resumed = paused.Resume(paused.Revision);
        Assert.Equal(PlayMatchPhase.Running, resumed.Phase);
        Assert.Equal(match.DefinitionRevision, resumed.DefinitionRevision);
    }

    [Fact]
    public void ReplayOnlyAllowedAfterFinalizationAndPreservesCommittedState()
    {
        var match = OpenNoTools();
        // Cannot replay a running match.
        Assert.Throws<InvalidOperationException>(() => match.Replay(match.Revision));

        // Build a committed checkpoint from the live state, then finalize and replay.
        var state = MatchState.Open(Match, Definition(), [new PlayContestant(Guid.NewGuid(), "Solo", "Human", "TeamA")]);
        var checkpoint = MatchCheckpoint.Commit(Guid.NewGuid(), Match, 1, state, new PlayClock(30, 120), []);

        // Simulate finalization by constructing a finalized match and replaying it.
        var finalized = new PlayMatch(Match, DefinitionID, 1, 1, PlayMatchPhase.Finalized, match.Rounds).Capture();
        var replaying = finalized.Replay(finalized.Revision);
        Assert.Equal(PlayMatchPhase.Replaying, replaying.Phase);

        // Replay leaves the committed definition pin intact and reconstructs identical state.
        Assert.True(PlayMatch.ReplayPreservesCommittedState(checkpoint, replaying));
        Assert.Equal(state.CurrentRound, checkpoint.Reconstruct().CurrentRound);
    }

    [Fact]
    public void StaleLifecycleTransitionNeverMutatesMatch()
    {
        var match = OpenNoTools();
        Assert.Throws<InvalidOperationException>(() => match.Pause(99));
        Assert.Equal(PlayMatchPhase.Running, match.Phase);
        Assert.Equal(1, match.Revision);
    }

    [Fact]
    public void MatchPinsDefinitionRevision()
    {
        var match = OpenNoTools();
        Assert.Equal(DefinitionID, match.DefinitionID);
        Assert.Equal(1, match.DefinitionRevision);
    }
}
