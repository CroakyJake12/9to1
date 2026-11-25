using Haven.Core.Games;

namespace Haven.Core.Tests;

/// <summary>IX-PLAY-001: answer selection is a local draft until Submit; only the one submitted value counts after reveal, and unrevealed
/// opponents stay hidden. Proves draft switching never scores, submit commits only the latest value, reconnect reconciles to one submitted
/// answer, and unrevealed opponents are not exposed.</summary>
public sealed class GamesPlayAnswerDraftTests
{
    private static readonly Guid Match = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Opponent = Guid.NewGuid();

    [Fact]
    public void DraftSwitchingNeverScoresAndOnlyLatestIsSubmitted()
    {
        var draft = PlayAnswerDraft.Begin(Match, 1, Me);
        Assert.Equal(PlayAnswerState.Draft, draft.State);
        Assert.False(draft.Counts);

        // Select B then switch to C; neither counts while drafting.
        var b = draft.Select("B", 1);
        var c = b.Select("C", b.Revision);
        Assert.Equal("B", b.SelectedOption);
        Assert.Equal("C", c.SelectedOption);
        Assert.False(c.Counts);

        // Submit commits exactly the latest value C.
        var submitted = c.Submit(c.Revision);
        Assert.Equal(PlayAnswerState.Submitted, submitted.State);
        Assert.Equal("C", submitted.SelectedOption);
    }

    [Fact]
    public void SubmitIsIdempotentReusingSubmissionReference()
    {
        var draft = PlayAnswerDraft.Begin(Match, 1, Me).Select("C", 1);
        var first = draft.Submit(draft.Revision);
        // Repeated activation cannot submit again; the same submission reference is retained.
        Assert.Throws<InvalidOperationException>(() => first.Submit(first.Revision));
    }

    [Fact]
    public void UncertainTimeoutRetainsOriginalCandidateAndReference()
    {
        var draft = PlayAnswerDraft.Begin(Match, 1, Me).Select("C", 1);
        var submitted = draft.Submit(draft.Revision);
        var reconciled = submitted.ReconcileFailure(submitted.Revision);

        // The original candidate and submission reference are preserved; no new answer is sent.
        Assert.Equal("C", reconciled.SelectedOption);
        Assert.Equal(submitted.SubmissionRef, reconciled.SubmissionRef);
        Assert.Equal(PlayAnswerState.Rejected, reconciled.State);
        Assert.False(reconciled.Counts);
    }

    [Fact]
    public void AcceptedAnswerCountsExactlyOnce()
    {
        var draft = PlayAnswerDraft.Begin(Match, 1, Me).Select("C", 1);
        var submitted = draft.Submit(draft.Revision);
        var accepted = submitted.Accept(submitted.Revision);
        Assert.True(accepted.Counts);
        // Accepting again is not possible; it cannot award points twice.
        Assert.Throws<InvalidOperationException>(() => accepted.Accept(accepted.Revision));
    }

    [Fact]
    public void UnrevealedOpponentAnswerIsNeverExposed()
    {
        var ledger = PlayAnswerLedger.Open(Match, 1);
        var mine = PlayAnswerDraft.Begin(Match, 1, Me).Select("C", 1);
        var opponent = PlayAnswerDraft.Begin(Match, 1, Opponent).Select("A", 1);
        ledger = ledger.Upsert(mine.Submit(mine.Revision)).Upsert(opponent.Submit(opponent.Revision));

        // While sealed, no submitted value is disclosed — including the opponent's.
        Assert.Null(ledger.SubmittedValueFor(Opponent));
        Assert.Null(ledger.SubmittedValueFor(Me));

        var revealed = ledger.Reveal();
        Assert.Equal("A", revealed.SubmittedValueFor(Opponent));
    }

    [Fact]
    public void ReconcileAfterRevealKeepsOneSubmittedAnswerAndScoresIt()
    {
        // Acceptance scenario: I submitted C (B was abandoned), round reveals, my one answer counts.
        var ledger = PlayAnswerLedger.Open(Match, 1);
        var mine = PlayAnswerDraft.Begin(Match, 1, Me).Select("B", 1);
        mine = mine.Select("C", mine.Revision);
        var accepted = mine.Submit(mine.Revision).Accept(mine.Revision + 1);
        ledger = ledger.Upsert(accepted);

        var revealed = ledger.Reveal();
        Assert.Equal("C", revealed.SubmittedValueFor(Me));
        // Only the one accepted C answer counts; B is never present.
        Assert.Single(revealed.ScoredAnswers());
        Assert.DoesNotContain(revealed.Answers, a => a.SelectedOption == "B");
    }

    [Fact]
    public void LockedAnswerRejectsSelectionAfterSubmit()
    {
        var draft = PlayAnswerDraft.Begin(Match, 1, Me).Select("C", 1);
        var submitted = draft.Submit(draft.Revision);
        // Submitting disables changes until acceptance or a reconciled failure.
        Assert.Throws<InvalidOperationException>(() => submitted.Select("D", submitted.Revision));
    }

    [Fact]
    public void UnselectedSubmitIsDisabled()
    {
        var draft = PlayAnswerDraft.Begin(Match, 1, Me);
        Assert.Throws<InvalidOperationException>(() => draft.Submit(draft.Revision));
    }
}
