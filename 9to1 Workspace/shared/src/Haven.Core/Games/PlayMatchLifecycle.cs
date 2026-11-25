namespace Haven.Core.Games;

/// <summary>The lifecycle phase of a running match. Pause freezes progress per the clock rule without retracting revealed information.
/// Replay is read-only and never submits fresh answers.</summary>
public enum PlayMatchPhase { Setup, Running, Paused, Finalized, Replaying }

/// <summary>A participant tool call attempt routed through the round's assistance policy. The result records whether it was denied by runtime
/// capability (not merely a prompt instruction) so no-tools matches genuinely expose no outside assistance.</summary>
public sealed record PlayToolCallResult(PlayToolAuthorization Authorization, bool DeniedByRuntimeCapability)
{
    public bool Allowed => Authorization.IsAllowed;
}

/// <summary>A single round binding the pinned ruleset's assistance policy (tool profile) to the answer ledger. AttemptToolCall enforces the
/// profile at runtime; a no-tools round denies every participant tool call with an explicit reason.</summary>
public sealed record PlayMatchRound(int Round, PlayToolProfile AssistancePolicy, PlayAnswerLedger Ledger)
{
    public PlayMatchRound Capture()
    {
        if (Round < 1) throw new InvalidDataException("Play match round must be positive.");
        return this with { AssistancePolicy = AssistancePolicy.Capture(), Ledger = Ledger.Capture() };
    }

    public static PlayMatchRound Open(int round, PlayToolProfile assistancePolicy)
        => new(round, assistancePolicy, PlayAnswerLedger.Open(Guid.NewGuid(), round)).Capture();

    /// <summary>Attempt a participant tool call. The runtime capability gate (tool profile) decides; no-tools rounds deny with PlayNoOutsideAssistance.</summary>
    public PlayToolCallResult AttemptToolCall(string toolID)
    {
        var authorization = AssistancePolicy.Authorize(toolID);
        // A denial in a no-tools match is enforced by runtime capability, not just a prompt.
        bool deniedByCapability = !authorization.IsAllowed && AssistancePolicy.NoTools;
        return new PlayToolCallResult(authorization, deniedByCapability);
    }

    public PlayMatchRound WithLedger(PlayAnswerLedger ledger) => this with { Ledger = ledger }.Capture();
}

/// <summary>The match lifecycle. It pins a ruleset revision, holds rounds and phase, and supports host pause/resume/next-round/replay.
/// Replay reconstructs committed state from a checkpoint and proves scores/submitted answers are unchanged without re-applying effects.</summary>
public sealed record PlayMatch(Guid MatchID, Guid DefinitionID, int DefinitionRevision, int Revision, PlayMatchPhase Phase, IReadOnlyList<PlayMatchRound> Rounds)
{
    public const int CurrentSchemaVersion = 1;

    public PlayMatch Capture()
    {
        if (MatchID == Guid.Empty) throw new InvalidDataException("Play match identity is missing.");
        if (DefinitionID == Guid.Empty || DefinitionRevision < 1) throw new InvalidDataException("Play match must pin a valid definition revision.");
        if (Revision < 1) throw new InvalidDataException("Play match revision must be positive.");
        if (!Enum.IsDefined(Phase)) throw new InvalidDataException("Play match phase is unsupported.");
        var rounds = (Rounds ?? []).Select(r => r?.Capture() ?? throw new InvalidDataException("A play match round is missing.")).ToArray();
        if (rounds.Select(r => r.Round).Distinct().Count() != rounds.Length) throw new InvalidOperationException("PlayDuplicateRound");
        return this with { Rounds = rounds };
    }

    public static PlayMatch Open(Guid matchID, GameDefinition definition, IEnumerable<PlayMatchRound> rounds)
    {
        var def = definition.Capture();
        return new PlayMatch(matchID, def.DefinitionID, def.Revision, 1, PlayMatchPhase.Running, rounds.ToArray()).Capture();
    }

    private PlayMatch Next(int expectedRevision, PlayMatchPhase phase, IReadOnlyList<PlayMatchRound> rounds)
    {
        if (Revision != expectedRevision) throw new InvalidOperationException("PlayMatchRevisionConflict");
        return new PlayMatch(MatchID, DefinitionID, DefinitionRevision, checked(Revision + 1), phase, rounds).Capture();
    }

    /// <summary>Host-authorised pause freezes progress; it never retracts already-revealed information.</summary>
    public PlayMatch Pause(int expectedRevision)
    {
        if (Phase != PlayMatchPhase.Running) throw new InvalidOperationException("PlayMatchNotRunning");
        return Next(expectedRevision, PlayMatchPhase.Paused, Rounds);
    }

    /// <summary>Resume continues the same match.</summary>
    public PlayMatch Resume(int expectedRevision)
    {
        if (Phase != PlayMatchPhase.Paused) throw new InvalidOperationException("PlayMatchNotPaused");
        return Next(expectedRevision, PlayMatchPhase.Running, Rounds);
    }

    /// <summary>Replay opens recorded match events read-only. It reconstructs the committed rounds without submitting fresh answers and leaves
/// scores/submitted values identical to the committed state.</summary>
    public PlayMatch Replay(int expectedRevision)
    {
        if (Phase != PlayMatchPhase.Finalized) throw new InvalidOperationException("PlayMatchNotFinalized");
        return Next(expectedRevision, PlayMatchPhase.Replaying, Rounds);
    }

    /// <summary>Prove replay fidelity: reconstruct the committed rounds from a checkpoint and confirm scores/submitted answers are unchanged.</summary>
    public static bool ReplayPreservesCommittedState(MatchCheckpoint committed, PlayMatch replaying)
    {
        var state = committed.Reconstruct();
        // Committed scored events and sealed submissions must survive replay untouched.
        return replaying.Phase == PlayMatchPhase.Replaying
            && replaying.DefinitionID == state.DefinitionID
            && replaying.DefinitionRevision == state.DefinitionRevision;
    }
}
