namespace Haven.Core.Games;

/// <summary>An external effect receipt recording that a side-effect (e.g. reveal broadcast, score publication) was applied once.
/// The IdempotencyKey lets replay deduplicate so an already-applied effect is never repeated.</summary>
public sealed record PlayEffectReceipt(string IdempotencyKey, string EffectKind, int Round)
{
    public PlayEffectReceipt Capture()
    {
        if (string.IsNullOrWhiteSpace(IdempotencyKey)) throw new InvalidDataException("Play effect receipt idempotency key is required.");
        if (string.IsNullOrWhiteSpace(EffectKind)) throw new InvalidDataException("Play effect receipt kind is required.");
        if (Round < 1) throw new InvalidDataException("Play effect receipt round must be positive.");
        return this;
    }
}

/// <summary>An immutable checkpoint of committed match state. It preserves sealed submissions, resolved rounds, the pinned definition revision,
/// clock policy and effect receipts so a reconnect can reconstruct exactly what was committed — without depending on live mutable state.</summary>
public sealed record MatchCheckpoint(Guid CheckpointID, Guid LineageID, int Sequence, MatchState State, PlayClock ClockPolicy, IReadOnlyList<PlayEffectReceipt> EffectReceipts)
{
    public const int CurrentSchemaVersion = 1;

    public MatchCheckpoint Capture()
    {
        if (CheckpointID == Guid.Empty) throw new InvalidDataException("Match checkpoint identity is missing.");
        if (LineageID == Guid.Empty) throw new InvalidDataException("Match checkpoint lineage is missing.");
        if (Sequence < 1) throw new InvalidDataException("Match checkpoint sequence must be positive.");
        if (State is null) throw new InvalidDataException("Match checkpoint state is missing.");
        var captured = State.Capture();
        ClockPolicy.Capture();
        var receipts = (EffectReceipts ?? []).Select(r => r?.Capture() ?? throw new InvalidDataException("A play effect receipt is missing.")).ToArray();
        if (receipts.Select(r => r.IdempotencyKey).Distinct(StringComparer.Ordinal).Count() != receipts.Length) throw new InvalidOperationException("PlayDuplicateEffectReceipt");
        return this with { State = captured, EffectReceipts = receipts };
    }

    public static MatchCheckpoint Commit(Guid checkpointID, Guid lineageID, int sequence, MatchState state, PlayClock clockPolicy, IEnumerable<PlayEffectReceipt> effectReceipts)
        => new(checkpointID, lineageID, sequence, state, clockPolicy, (effectReceipts ?? []).ToArray()).Capture();

    /// <summary>Reconstruct the committed match state on reconnect. Returns the exact pinned state; it never re-runs external effects.</summary>
    public MatchState Reconstruct() => State.Capture();

    /// <summary>Whether an effect with this idempotency key was already recorded, so replay can skip repeating it.</summary>
    public bool HasEffect(string idempotencyKey) => EffectReceipts.Any(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
}

/// <summary>A checkpoint lineage (a match's committed history). Branching creates a new lineage that never mutates the original history;
/// appending only adds to the branch. Definition edits cannot rewrite an active match because each checkpoint pins its own definition revision.</summary>
public sealed record MatchLineage(Guid LineageID, Guid DefinitionID, int DefinitionRevision, IReadOnlyList<MatchCheckpoint> Checkpoints)
{
    public MatchLineage Capture()
    {
        if (LineageID == Guid.Empty) throw new InvalidDataException("Match lineage identity is missing.");
        if (DefinitionID == Guid.Empty || DefinitionRevision < 1) throw new InvalidDataException("Match lineage must pin a valid definition revision.");
        var checkpoints = (Checkpoints ?? []).Select(c => c?.Capture() ?? throw new InvalidDataException("A match checkpoint is missing.")).ToArray();
        if (checkpoints.Any(c => c.LineageID != LineageID)) throw new InvalidOperationException("MatchCheckpointLineageMismatch");
        if (checkpoints.Select(c => c.Sequence).Distinct().Count() != checkpoints.Length) throw new InvalidOperationException("MatchDuplicateCheckpointSequence");
        return this with { Checkpoints = checkpoints };
    }

    public static MatchLineage Begin(Guid lineageID, GameDefinition definition, IEnumerable<PlayContestant> contestants, PlayClock clockPolicy)
    {
        var def = definition.Capture();
        var state = MatchState.Open(lineageID, def, contestants);
        var first = MatchCheckpoint.Commit(Guid.NewGuid(), lineageID, 1, state, clockPolicy, Array.Empty<PlayEffectReceipt>());
        return new MatchLineage(lineageID, def.DefinitionID, def.Revision, [first]).Capture();
    }

    /// <summary>Append a committed checkpoint to this lineage. The original checkpoints are preserved; only the sequence advances.</summary>
    public MatchLineage Append(MatchCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var captured = checkpoint.Capture();
        if (captured.LineageID != LineageID) throw new InvalidOperationException("MatchCheckpointLineageMismatch");
        int expected = Checkpoints.Count == 0 ? 1 : Checkpoints.Max(c => c.Sequence) + 1;
        if (captured.Sequence != expected) throw new InvalidOperationException("MatchCheckpointSequenceGap");
        return new MatchLineage(LineageID, DefinitionID, DefinitionRevision, [.. Checkpoints, captured]).Capture();
    }

    /// <summary>Branch a new lineage from the latest checkpoint. The new lineage carries the committed state forward but has its own identity,
/// so subsequent appends never mutate this original history.</summary>
    public MatchLineage Branch(Guid newLineageID)
    {
        if (newLineageID == Guid.Empty || newLineageID == LineageID) throw new InvalidDataException("A branch requires a distinct lineage identity.");
        var head = Checkpoints.Count == 0 ? throw new InvalidOperationException("MatchCheckpointEmpty") : Checkpoints[^1];
        // Re-pin the carried state under the new lineage as sequence 1; original checkpoints are untouched.
        var carried = MatchCheckpoint.Commit(Guid.NewGuid(), newLineageID, 1, head.State, head.ClockPolicy, head.EffectReceipts);
        return new MatchLineage(newLineageID, DefinitionID, DefinitionRevision, [carried]).Capture();
    }

    /// <summary>Replay a candidate effect against the latest checkpoint: returns false (and does not change history) when the effect was already applied.</summary>
    public bool TryApplyEffect(string idempotencyKey, string effectKind, int round)
    {
        var head = Checkpoints.Count == 0 ? throw new InvalidOperationException("MatchCheckpointEmpty") : Checkpoints[^1];
        return !head.HasEffect(idempotencyKey);
    }

    /// <summary>Record a newly-applied external effect on the latest checkpoint, producing an advanced lineage. Repeating an existing key is rejected.</summary>
    public MatchLineage RecordEffect(string idempotencyKey, string effectKind, int round)
    {
        var head = Checkpoints.Count == 0 ? throw new InvalidOperationException("MatchCheckpointEmpty") : Checkpoints[^1];
        if (head.HasEffect(idempotencyKey)) throw new InvalidOperationException("PlayEffectAlreadyApplied");
        var receipt = new PlayEffectReceipt(idempotencyKey, effectKind, round);
        var updatedHead = head with { EffectReceipts = [.. head.EffectReceipts, receipt] };
        // Replace the head checkpoint in place (same sequence) so history length is stable; earlier checkpoints are preserved.
        var checkpoints = Checkpoints.Take(Checkpoints.Count - 1).Append(updatedHead).ToArray();
        return new MatchLineage(LineageID, DefinitionID, DefinitionRevision, checkpoints).Capture();
    }

    public MatchState CurrentState() => (Checkpoints.Count == 0 ? throw new InvalidOperationException("MatchCheckpointEmpty") : Checkpoints[^1]).Reconstruct();
}
