using Haven.Core.Games;

namespace Haven.Core.Tests;

/// <summary>ED-PLAY-001: a versioned GameDefinition specifies state schema, contestants/teams, legal typed actions, visibility, turns/rounds,
/// clocks, scoring and winner rules. Proves versioned identity, legal-action/visibility validation and stale-edit no-mutation.</summary>
public sealed class GamesPlayGameDefinitionTests
{
    private static readonly Guid DefinitionID = Guid.NewGuid();

    private static GameDefinition MakeDefinition() => GameDefinition.Create(DefinitionID,
        [new PlayStateField("Question", "string", PlayVisibility.Public), new PlayStateField("Answer", "string", PlayVisibility.Judge)],
        [new PlayRole("Human", IsPersistent: true), new PlayRole("SessionAgent", IsPersistent: false)],
        ["TeamA", "TeamB"], new PlayClock(30, 120), "sum-of-rounds", "highest-score");

    [Fact]
    public void CreateAssignsVersionedIdentity()
    {
        var definition = MakeDefinition();
        Assert.Equal(DefinitionID, definition.DefinitionID);
        Assert.Equal(1, definition.Revision);
        Assert.Equal(2, definition.StateSchema.Count);
    }

    [Fact]
    public void AddActionRejectsDuplicateNameAndUnknownRole()
    {
        var definition = MakeDefinition().AddAction(new PlayAction("Submit", "answer", ["Human", "SessionAgent"]), 1);
        Assert.Equal(2, definition.Revision);
        Assert.Throws<InvalidOperationException>(() => definition.AddAction(new PlayAction("Submit", "answer", ["Human"]), 2));
        // A role not declared in the definition is rejected.
        Assert.Throws<InvalidOperationException>(() => definition.AddAction(new PlayAction("Reveal", "round", ["Ghost"]), 2));
    }

    [Fact]
    public void DuplicateStateFieldRoleAndTeamAreRejected()
    {
        var definition = MakeDefinition();
        Assert.Throws<InvalidOperationException>(() => definition.AddStateField(new PlayStateField("Question", "string", PlayVisibility.Public), 1));
        Assert.Throws<InvalidOperationException>(() => definition.AddRole(new PlayRole("Human", true), 1));
        Assert.Throws<InvalidOperationException>(() => definition.AddTeam("TeamA", 1));
    }

    [Fact]
    public void StaleEditNeverMutatesDefinition()
    {
        var definition = MakeDefinition();
        Assert.Throws<InvalidOperationException>(() => definition.AddAction(new PlayAction("Submit", "answer", ["Human"]), 99));
        Assert.Equal(1, definition.Revision);
        Assert.Empty(definition.Actions);
    }

    [Fact]
    public void AuthorisedViewRemovesHiddenInformationPerAudience()
    {
        var definition = MakeDefinition();
        // A contestant sees Public + Self/Team but not Judge-only answers.
        var contestant = definition.AuthorisedView(isContestant: true, isJudge: false);
        Assert.Contains(contestant, f => f.Name == "Question");
        Assert.DoesNotContain(contestant, f => f.Name == "Answer");

        // A judge additionally sees Judge-visibility fields.
        var judge = definition.AuthorisedView(isContestant: false, isJudge: true);
        Assert.Contains(judge, f => f.Name == "Answer");

        // A spectator (neither contestant nor judge) sees only Public fields.
        var spectator = definition.AuthorisedView(isContestant: false, isJudge: false);
        Assert.Single(spectator);
        Assert.Equal("Question", spectator[0].Name);
    }

    [Fact]
    public void SetClockRejectsNonPositiveAndStaleRevision()
    {
        var definition = MakeDefinition();
        Assert.Throws<InvalidDataException>(() => new PlayClock(0, 120).Capture());
        Assert.Throws<InvalidOperationException>(() => definition.SetClock(new PlayClock(15, 60), 99));
        var updated = definition.SetClock(new PlayClock(15, 60), 1);
        Assert.Equal(15, updated.Clock.TurnSeconds);
    }

    [Fact]
    public void MissingScoringOrWinnerRuleIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => GameDefinition.Create(DefinitionID, [], [new PlayRole("Human", true)], ["TeamA"], new PlayClock(30, 120), " ", "highest-score"));
        Assert.Throws<InvalidDataException>(() => GameDefinition.Create(DefinitionID, [], [new PlayRole("Human", true)], ["TeamA"], new PlayClock(30, 120), "sum", " "));
    }
}
