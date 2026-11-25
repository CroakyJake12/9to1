namespace Haven.Core.Games;

/// <summary>The lifecycle of a contestant's answer for one round. Draft is local and unscored; Submitted is the committed match event.
/// Accepted confirms it counts once; Rejected/Reconciled resolve an uncertain timeout without sending a new answer.</summary>
public enum PlayAnswerState { Draft, Submitted, Accepted, Rejected }

/// <summary>A per-contestant-per-round answer draft. Selecting replaces the local candidate freely and is NOT scored until Submit.
/// Submit atomically commits exactly one submitted value (the latest draft) under a stable submission reference so repeated activation reuses it.</summary>
public sealed record PlayAnswerDraft(Guid MatchID, int Round, Guid ContestantID, int Revision, string? SelectedOption, PlayAnswerState State, Guid SubmissionRef)
{
    public PlayAnswerDraft Capture()
    {
        if (MatchID == Guid.Empty) throw new InvalidDataException("Play answer match identity is missing.");
        if (ContestantID == Guid.Empty) throw new InvalidDataException("Play answer contestant identity is missing.");
        if (Round < 1) throw new InvalidDataException("Play answer round must be positive.");
        if (Revision < 1) throw new InvalidDataException("Play answer revision must be positive.");
        if (!Enum.IsDefined(State)) throw new InvalidDataException("Play answer state is unsupported.");
        // A submitted/accepted/rejected answer must carry a committed value and a submission reference.
        if (State != PlayAnswerState.Draft && string.IsNullOrWhiteSpace(SelectedOption)) { throw new InvalidOperationException("PlayAnswerSubmittedWithoutValue"); }
        return this;
    }

    public static PlayAnswerDraft Begin(Guid matchID, int round, Guid contestantID)
        => (new PlayAnswerDraft(matchID, round, contestantID, 1, null, PlayAnswerState.Draft, Guid.NewGuid())).Capture();

    /// <summary>Select or replace the local candidate. Only legal while Draft; a submitted answer is locked until acceptance/reconciled failure.</summary>
    public PlayAnswerDraft Select(string option, int expectedRevision)
    {
        if (string.IsNullOrWhiteSpace(option)) throw new InvalidDataException("An answer option is required.");
        if (State != PlayAnswerState.Draft) { throw new InvalidOperationException("PlayAnswerLockedAfterSubmit"); }
        if (Revision != expectedRevision) { throw new InvalidOperationException("PlayAnswerRevisionConflict"); }
        return (this with { SelectedOption = option, Revision = checked(Revision + 1) }).Capture();
    }

    /// <summary>Submit the current draft as the single committed answer. Repeated submission reuses the same SubmissionRef (idempotent).</summary>
    public PlayAnswerDraft Submit(int expectedRevision)
    {
        if (State != PlayAnswerState.Draft) { throw new InvalidOperationException("PlayAnswerAlreadySubmitted"); }
        if (string.IsNullOrWhiteSpace(SelectedOption)) { throw new InvalidOperationException("PlayAnswerUnselectedSubmitDisabled"); }
        if (Revision != expectedRevision) { throw new InvalidOperationException("PlayAnswerRevisionConflict"); }
        return (this with { State = PlayAnswerState.Submitted, Revision = checked(Revision + 1) }).Capture();
    }

    /// <summary>Accept the submitted answer so it counts exactly once. A late/closed round rejects with canonical state.</summary>
    public PlayAnswerDraft Accept(int expectedRevision)
    {
        if (State != PlayAnswerState.Submitted) { throw new InvalidOperationException("PlayAnswerNotSubmitted"); }
        if (Revision != expectedRevision) { throw new InvalidOperationException("PlayAnswerRevisionConflict"); }
        return (this with { State = PlayAnswerState.Accepted, Revision = checked(Revision + 1) }).Capture();
    }

    /// <summary>An uncertain timeout retains the original candidate and submission reference rather than sending a new answer.</summary>
    public PlayAnswerDraft ReconcileFailure(int expectedRevision)
    {
        if (State != PlayAnswerState.Submitted) { throw new InvalidOperationException("PlayAnswerNotSubmitted"); }
        if (Revision != expectedRevision) { throw new InvalidOperationException("PlayAnswerRevisionConflict"); }
        // Keep the same SelectedOption and SubmissionRef; only the state reflects the unresolved attempt.
        return (this with { State = PlayAnswerState.Rejected, Revision = checked(Revision + 1) }).Capture();
    }

    /// <summary>Whether this answer contributes to score: only an Accepted answer counts, and never twice.</summary>
    public bool Counts => State == PlayAnswerState.Accepted;
}

/// <summary>The reveal state of a round. Opponents' answers stay hidden until the rules permit the round to close.</summary>
public enum PlayRevealState { Sealed, Revealed }

/// <summary>A round's answer ledger. It holds each contestant's draft and only exposes submitted values after Reveal; clicking an unrevealed
/// opponent never discloses their answer. Scoring basis is available only after release.</summary>
public sealed record PlayAnswerLedger(Guid MatchID, int Round, PlayRevealState RevealState, IReadOnlyList<PlayAnswerDraft> Answers)
{
    public PlayAnswerLedger Capture()
    {
        if (MatchID == Guid.Empty) throw new InvalidDataException("Play answer ledger match identity is missing.");
        if (Round < 1) throw new InvalidDataException("Play answer ledger round must be positive.");
        if (!Enum.IsDefined(RevealState)) throw new InvalidDataException("Play reveal state is unsupported.");
        var answers = (Answers ?? []).Select(a => a?.Capture() ?? throw new InvalidDataException("A play answer is missing.")).ToArray();
        if (answers.Select(a => a.ContestantID).Distinct().Count() != answers.Length) throw new InvalidOperationException("PlayDuplicateAnswerContestant");
        return this with { Answers = answers };
    }

    public static PlayAnswerLedger Open(Guid matchID, int round) =>
        new(matchID, round, PlayRevealState.Sealed, Array.Empty<PlayAnswerDraft>()).Capture();

    private PlayAnswerLedger Next(PlayRevealState reveal, IReadOnlyList<PlayAnswerDraft> answers)
        => new(MatchID, Round, reveal, answers).Capture();

    public PlayAnswerLedger Upsert(PlayAnswerDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var captured = draft.Capture();
        if (captured.MatchID != MatchID || captured.Round != Round) throw new InvalidOperationException("PlayAnswerRoundMismatch");
        var exists = Answers.Any(a => a.ContestantID == captured.ContestantID);
        var answers = exists
            ? Answers.Select(a => a.ContestantID == captured.ContestantID ? captured : a).ToArray()
            : [.. Answers, captured];
        return Next(RevealState, answers);
    }

    /// <summary>Close the round per the rules: reveal is a separate transition from submission.</summary>
    public PlayAnswerLedger Reveal() => Next(PlayRevealState.Revealed, Answers);

    /// <summary>The submitted value for a contestant, or null when unrevealed. Unrevealed opponents never disclose their answer.</summary>
    public string? SubmittedValueFor(Guid contestantID)
        => RevealState == PlayRevealState.Sealed ? null : Answers.SingleOrDefault(a => a.ContestantID == contestantID)?.SelectedOption;

    /// <summary>The scoring basis after release: only Accepted answers count, each exactly once.</summary>
    public IReadOnlyList<PlayAnswerDraft> ScoredAnswers() => Answers.Where(a => a.Counts).ToArray();
}
