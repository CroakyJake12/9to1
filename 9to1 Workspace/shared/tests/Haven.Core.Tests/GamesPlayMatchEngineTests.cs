using Haven.Core.Games;

namespace Haven.Core.Tests;

/// <summary>ED-PLAY-002: the engine validates action availability, turn ownership, prerequisites and timing before changing MatchState;
/// simultaneous submissions stay sealed until resolution; scores derive from accepted events only.
/// Proves illegal-action/ownership rejection, sealed duplicate-answer rejection, and score-from-accepted-events.</summary>
public sealed class GamesPlayMatchEngineTests
{
    private static readonly Guid DefinitionID = Guid.NewGuid();
    private static readonly Guid MatchID = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static GameDefinition Definition() => GameDefinition.Create(DefinitionID,
        [new PlayStateField("Question", "string", PlayVisibility.Public), new PlayStateField("Answer", "string", PlayVisibility.Judge)],
        [new PlayRole("Human", true)], ["TeamA", "TeamB"], new PlayClock(30, 120), "sum-of-rounds", "highest-score")
        .AddAction(new PlayAction("Submit", "answer", ["Human"]), 1);

    private static MatchState Open() => MatchState.Open(MatchID, Definition(),
        [new PlayContestant(Alice, "Alice", "Human", "TeamA"), new PlayContestant(Bob, "Bob", "Human", "TeamB")]);

    private static PlaySubmission Submission(Guid contestant, string value) =>
        new(Guid.NewGuid(), contestant, 1, "Submit", value, DateTimeOffset.UtcNow);

    [Fact]
    public void OpenPinsDefinitionRevision()
    {
        var match = Open();
        Assert.Equal(DefinitionID, match.DefinitionID);
        Assert.Equal(2, match.DefinitionRevision);
        Assert.Equal(1, match.CurrentRound);
    }

    [Fact]
    public void SubmitRejectsUnknownContestantAndIllegalAction()
    {
        var definition = Definition();
        var match = Open();
        Assert.Throws<InvalidOperationException>(() => match.Submit(definition, 1, Submission(Guid.NewGuid(), "x")));
        // An action not declared in the pinned definition is illegal.
        Assert.Throws<InvalidOperationException>(() => match.Submit(definition, 1, new PlaySubmission(Guid.NewGuid(), Alice, 1, "Reveal", "x", DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void SubmitRejectsWrongRoundTiming()
    {
        var definition = Definition();
        var match = Open();
        // Round 2 submission while the match is on round 1 is rejected.
        Assert.Throws<InvalidOperationException>(() => match.Submit(definition, 1, new PlaySubmission(Guid.NewGuid(), Alice, 2, "Submit", "x", DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void SealedRoundRejectsDuplicateSubmissionBySameContestant()
    {
        var definition = Definition();
        var once = Open().Submit(definition, 1, Submission(Alice, "B"));
        Assert.Single(once.SealedSubmissions);
        // A second answer from Alice in the same sealed round is rejected.
        Assert.Throws<InvalidOperationException>(() => once.Submit(definition, 2, Submission(Alice, "C")));
    }

    [Fact]
    public void SubmissionsStaySealedUntilResolve()
    {
        var definition = Definition();
        var submitted = Open().Submit(definition, 1, Submission(Alice, "B")).Submit(definition, 2, Submission(Bob, "B"));
        // Before resolution there are no scored events; nothing is revealed.
        Assert.Empty(submitted.ScoredEvents);

        var resolved = submitted.Resolve(3, correctAnswer: "B", points: 10);
        Assert.Equal(2, resolved.CurrentRound);
        Assert.Equal(2, resolved.ScoredEvents.Count);
    }

    [Fact]
    public void ScoresDeriveOnlyFromAcceptedEvents()
    {
        var definition = Definition();
        // Alice answers correctly (B), Bob answers incorrectly (A).
        var submitted = Open().Submit(definition, 1, Submission(Alice, "B")).Submit(definition, 2, Submission(Bob, "A"));
        var resolved = submitted.Resolve(3, correctAnswer: "B", points: 10);

        var scores = resolved.TeamScores();
        Assert.Equal(10, scores["TeamA"]);
        // Bob's wrong answer produced no scored event; TeamB has zero.
        Assert.False(scores.ContainsKey("TeamB") && scores["TeamB"] != 0);
    }

    [Fact]
    public void StaleTransitionNeverMutatesMatch()
    {
        var definition = Definition();
        var match = Open();
        Assert.Throws<InvalidOperationException>(() => match.Submit(definition, 99, Submission(Alice, "B")));
        Assert.Equal(1, match.Revision);
        Assert.Empty(match.SealedSubmissions);
    }

    [Fact]
    public void DefinitionRevisionMismatchIsRejected()
    {
        var match = Open();
        // A definition at a different revision than the pinned one cannot drive transitions.
        var otherDefinition = GameDefinition.Create(Guid.NewGuid(), [], [new PlayRole("Human", true)], ["TeamA"], new PlayClock(30, 120), "sum", "high");
        Assert.Throws<InvalidOperationException>(() => match.Submit(otherDefinition, 1, Submission(Alice, "B")));
    }
}
