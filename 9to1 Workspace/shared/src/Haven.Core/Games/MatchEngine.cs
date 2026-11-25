namespace Haven.Core.Games;

/// <summary>A contestant in a match. Retains canonical identity and belongs to exactly one team with one role.</summary>
public sealed record PlayContestant(Guid ContestantID, string Name, string Role, string Team)
{
    public PlayContestant Capture()
    {
        if (ContestantID == Guid.Empty) throw new InvalidDataException("Play contestant identity is missing.");
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Play contestant name is required.");
        if (string.IsNullOrWhiteSpace(Role)) throw new InvalidDataException("Play contestant role is required.");
        if (string.IsNullOrWhiteSpace(Team)) throw new InvalidDataException("Play contestant team is required.");
        return this;
    }
}

/// <summary>A sealed submission captured during a round. It stays hidden until the round resolves; reveal is a separate transition.
/// The authoritative submission timestamp governs speed scoring.</summary>
public sealed record PlaySubmission(Guid SubmissionID, Guid ContestantID, int Round, string ActionName, string Value, DateTimeOffset SubmittedAt)
{
    public PlaySubmission Capture()
    {
        if (SubmissionID == Guid.Empty) throw new InvalidDataException("Play submission identity is missing.");
        if (ContestantID == Guid.Empty) throw new InvalidDataException("Play submission contestant is missing.");
        if (Round < 1) throw new InvalidDataException("Play submission round must be positive.");
        if (string.IsNullOrWhiteSpace(ActionName)) throw new InvalidDataException("Play submission action name is required.");
        return this;
    }
}

/// <summary>An accepted event that actually contributes to score. Scores derive from these, never from narration or unaccepted submissions.</summary>
public sealed record PlayScoredEvent(Guid EventID, int Round, Guid ContestantID, string Team, int Points);

/// <summary>The immutable match state pinned to a specific definition revision. Transitions validate before changing state and never mutate the original.</summary>
public sealed record MatchState(Guid MatchID, Guid DefinitionID, int DefinitionRevision, int Revision, int CurrentRound,
    IReadOnlyList<PlayContestant> Contestants, IReadOnlyList<PlaySubmission> SealedSubmissions, IReadOnlyList<PlayScoredEvent> ScoredEvents)
{
    public const int CurrentSchemaVersion = 1;

    public MatchState Capture()
    {
        if (MatchID == Guid.Empty) throw new InvalidDataException("Match identity is missing.");
        if (DefinitionID == Guid.Empty || DefinitionRevision < 1) throw new InvalidDataException("Match must pin a valid definition revision.");
        if (Revision < 1) throw new InvalidDataException("Match revision must be positive.");
        if (CurrentRound < 1) throw new InvalidDataException("Match round must be positive.");
        if (Contestants is null || SealedSubmissions is null || ScoredEvents is null) throw new InvalidDataException("Match structure is missing.");
        var contestants = Contestants.Select(c => c?.Capture() ?? throw new InvalidDataException("A play contestant is missing.")).ToArray();
        if (contestants.Select(c => c.ContestantID).Distinct().Count() != contestants.Length) throw new InvalidOperationException("PlayDuplicateContestant");
        var submissions = SealedSubmissions.Select(s => s?.Capture() ?? throw new InvalidDataException("A play submission is missing.")).ToArray();
        if (submissions.Select(s => s.SubmissionID).Distinct().Count() != submissions.Length) throw new InvalidOperationException("PlayDuplicateSubmission");
        var events = ScoredEvents.ToArray();
        if (events.Select(e => e.EventID).Distinct().Count() != events.Length) throw new InvalidOperationException("PlayDuplicateScoredEvent");
        return this with { Contestants = contestants, SealedSubmissions = submissions, ScoredEvents = events };
    }

    public static MatchState Open(Guid matchID, GameDefinition definition, IEnumerable<PlayContestant> contestants)
    {
        var captured = definition.Capture();
        return new MatchState(matchID, captured.DefinitionID, captured.Revision, 1, 1, contestants.ToArray(), Array.Empty<PlaySubmission>(), Array.Empty<PlayScoredEvent>()).Capture();
    }

    private MatchState Next(int expectedRevision, int currentRound, IReadOnlyList<PlayContestant> contestants,
        IReadOnlyList<PlaySubmission> submissions, IReadOnlyList<PlayScoredEvent> events)
    {
        if (Revision != expectedRevision) throw new InvalidOperationException("MatchRevisionConflict");
        return new MatchState(MatchID, DefinitionID, DefinitionRevision, checked(Revision + 1), currentRound, contestants, submissions, events).Capture();
    }

    /// <summary>Validate a submission before it changes state. Rejects unknown contestant, an action not legal for the acting role, wrong round timing,
/// and a duplicate submission by the same contestant in the same sealed round. Returns the new sealed state; nothing is revealed here.</summary>
    public MatchState Submit(GameDefinition definition, int expectedRevision, PlaySubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var captured = submission.Capture();
        var def = definition.Capture();
        if (def.DefinitionID != DefinitionID || def.Revision != DefinitionRevision) throw new InvalidOperationException("MatchDefinitionMismatch");

        var contestant = Contestants.SingleOrDefault(c => c.ContestantID == captured.ContestantID)
            ?? throw new InvalidOperationException("PlayUnknownContestant");
        var action = def.Actions.SingleOrDefault(a => string.Equals(a.Name, captured.ActionName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("PlayActionNotLegal");
        if (!action.AllowedRoles.Contains(contestant.Role, StringComparer.Ordinal)) throw new InvalidOperationException("PlayActionRoleNotAllowed");
        if (captured.Round != CurrentRound) throw new InvalidOperationException("PlayWrongRoundTiming");
        // A contestant may not submit twice into the same sealed round.
        if (SealedSubmissions.Any(s => s.ContestantID == captured.ContestantID && s.Round == CurrentRound)) throw new InvalidOperationException("PlayDuplicateSubmissionInRound");

        return Next(expectedRevision, CurrentRound, Contestants, SealedSubmissions.Append(captured).ToArray(), ScoredEvents);
    }

    /// <summary>Resolve the current round: reveal is a separate transition that converts accepted sealed submissions into scored events using the
/// definition's deterministic evaluation (here: exact match against the recorded answer), then advances the round. Submissions stay sealed until now.</summary>
    public MatchState Resolve(int expectedRevision, string correctAnswer, int points)
    {
        if (points < 0) throw new InvalidDataException("Scored event points may not be negative.");
        var newlyScored = SealedSubmissions.Where(s => s.Round == CurrentRound && string.Equals(s.Value, correctAnswer, StringComparison.Ordinal))
            .Select((s, index) => new PlayScoredEvent(Guid.NewGuid(), CurrentRound, s.ContestantID, Contestants.Single(c => c.ContestantID == s.ContestantID).Team, points))
            .ToArray();
        return Next(expectedRevision, checked(CurrentRound + 1), Contestants, SealedSubmissions, [.. ScoredEvents, .. newlyScored]);
    }

    /// <summary>Aggregate score per team derived only from accepted scored events.</summary>
    public IReadOnlyDictionary<string, int> TeamScores() =>
        ScoredEvents.GroupBy(e => e.Team).ToDictionary(g => g.Key, g => g.Sum(e => e.Points), StringComparer.Ordinal);
}
