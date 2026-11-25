using Haven.Core.Games;

namespace Haven.Core.Tests;

/// <summary>atlas:play / APP-PLAY-001: submit separates draft/score; hidden answers and no-tools rules follow actual match state.
/// Proves drafts never appear on the leaderboard, standings derive only from accepted events, hidden answers stay unexposed,
/// and replay recomputes identical standings.</summary>
public sealed class GamesPlayLeaderboardTests
{
    private static readonly Guid DefinitionID = Guid.NewGuid();
    private static readonly Guid Match = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static GameDefinition Definition() => GameDefinition.Create(DefinitionID,
        [new PlayStateField("Question", "string", PlayVisibility.Public), new PlayStateField("Answer", "string", PlayVisibility.Judge)],
        [new PlayRole("Human", true)], ["TeamA", "TeamB"], new PlayClock(30, 120), "sum-of-rounds", "highest-score")
        .AddAction(new PlayAction("Submit", "answer", ["Human"]), 1);

    private static MatchState Committed()
    {
        var definition = Definition();
        var state = MatchState.Open(Match, definition, [new PlayContestant(Alice, "Alice", "Human", "TeamA"), new PlayContestant(Bob, "Bob", "Human", "TeamB")]);
        var submitted = state.Submit(definition, 1, new PlaySubmission(Guid.NewGuid(), Alice, 1, "Submit", "B", DateTimeOffset.UtcNow))
            .Submit(definition, 2, new PlaySubmission(Guid.NewGuid(), Bob, 1, "Submit", "A", DateTimeOffset.UtcNow));
        return submitted.Resolve(submitted.Revision, correctAnswer: "B", points: 10);
    }

    [Fact]
    public void StandingsDeriveOnlyFromAcceptedEvents()
    {
        var leaderboard = PlayLeaderboard.From(Committed());
        // Alice (TeamA) answered correctly and scored; Bob's wrong answer produced no event.
        Assert.Equal(1, leaderboard.RankOf("TeamA"));
        Assert.Equal(0, leaderboard.Rows.Single(r => r.Team == "TeamB").Score);
    }

    [Fact]
    public void DraftOrSealedSubmissionNeverAppearsBeforeResolution()
    {
        var definition = Definition();
        var state = MatchState.Open(Match, definition, [new PlayContestant(Alice, "Alice", "Human", "TeamA"), new PlayContestant(Bob, "Bob", "Human", "TeamB")]);
        // Submissions are sealed but the round is not resolved: no scored events exist.
        var sealedOnly = state.Submit(definition, 1, new PlaySubmission(Guid.NewGuid(), Alice, 1, "Submit", "B", DateTimeOffset.UtcNow));
        var leaderboard = PlayLeaderboard.From(sealedOnly);

        // Both teams appear at zero; the sealed answer contributes nothing to score.
        Assert.All(leaderboard.Rows, row => Assert.Equal(0, row.Score));
    }

    [Fact]
    public void LeaderboardExposesNoPrivateAnswerContent()
    {
        var leaderboard = PlayLeaderboard.From(Committed());
        // Rows carry only team/score/rank — there is no field for answers or model context.
        Assert.All(leaderboard.Rows, row => Assert.False(string.IsNullOrEmpty(row.Team)));
        Assert.DoesNotContain("B", string.Join(",", leaderboard.Rows.Select(r => r.Team)));
    }

    [Fact]
    public void ReplayRecomputesIdenticalStandings()
    {
        var committed = Committed();
        var first = PlayLeaderboard.From(committed);
        // Reconstruct from a checkpoint and recompute; standings must be byte-identical.
        var checkpoint = MatchCheckpoint.Commit(Guid.NewGuid(), Match, 1, committed, new PlayClock(30, 120), []);
        var replayed = PlayLeaderboard.From(checkpoint.Reconstruct());

        Assert.Equal(first.Rows.Select(r => (r.Team, r.Score, r.Rank)), replayed.Rows.Select(r => (r.Team, r.Score, r.Rank)));
    }

    [Fact]
    public void TiesBreakDeterministicallyByName()
    {
        var definition = Definition();
        // Two teams with equal score must order by team name for a stable tie-break.
        var state = MatchState.Open(Match, definition, [new PlayContestant(Alice, "Alice", "Human", "TeamB"), new PlayContestant(Bob, "Bob", "Human", "TeamA")]);
        var submitted = state.Submit(definition, 1, new PlaySubmission(Guid.NewGuid(), Alice, 1, "Submit", "B", DateTimeOffset.UtcNow))
            .Submit(definition, 2, new PlaySubmission(Guid.NewGuid(), Bob, 1, "Submit", "B", DateTimeOffset.UtcNow));
        var resolved = submitted.Resolve(submitted.Revision, correctAnswer: "B", points: 5);

        var leaderboard = PlayLeaderboard.From(resolved);
        Assert.Equal(5, leaderboard.Rows[0].Score);
        Assert.Equal(5, leaderboard.Rows[1].Score);
        // Equal scores order TeamA before TeamB.
        Assert.Equal("TeamA", leaderboard.Rows[0].Team);
        Assert.Equal("TeamB", leaderboard.Rows[1].Team);
    }

    [Fact]
    public void DuplicateTeamRowIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => new PlayLeaderboard(PlayLeaderboardOrder.ScoreThenName,
            [new PlayLeaderboardRow("TeamA", 1, 1), new PlayLeaderboardRow("TeamA", 2, 2)]).Capture());
    }
}
