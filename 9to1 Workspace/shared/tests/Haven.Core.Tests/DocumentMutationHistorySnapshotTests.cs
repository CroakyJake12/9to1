using System.Text.Json;
using Haven.Application;

namespace Haven.Core.Tests;

public sealed class DocumentMutationHistorySnapshotTests
{
    [Fact]
    public void Same_engine_restore_retains_original_operation_ids_names_origins_order_and_redo()
    {
        var owner = NewOwner();
        owner.Apply("First", DocumentOperationOrigin.User, state => { state.Value = 1; state.Revision++; }, "native");
        var first = owner.LastOperation!;
        owner.Apply("Second", DocumentOperationOrigin.Ai, state => { state.Value = 2; state.Revision++; }, "assistant");
        var second = owner.LastOperation!;
        owner.Apply("Third", DocumentOperationOrigin.User, state => { state.Value = 3; state.Revision++; });
        var third = owner.LastOperation!;
        Assert.True(owner.Undo());
        var undo = owner.LastOperation;
        var payload = Bytes(owner.Current);
        var history = Capture(owner);
        Assert.Equal(new[] { first.Id, second.Id }, history.Undo.Select(frame => frame.Operation!.Id));
        Assert.Equal(third.Id, Assert.Single(history.Redo).Operation!.Id);
        var restored = Restore(history, owner.Current);
        Assert.Equal(undo, restored.LastOperation);
        Assert.True(restored.CanUndo); Assert.True(restored.CanRedo);
        Assert.True(restored.Redo()); Assert.Equal(3, restored.Current.Value);
        var roundtrip = Capture(restored);
        Assert.Equal(new[] { first, second, third }, roundtrip.Undo.Select(frame => frame.Operation));
        Assert.True(restored.Undo()); Assert.Equal(2, restored.Current.Value);
        Assert.True(restored.Undo()); Assert.Equal(1, restored.Current.Value);
        Assert.True(restored.Undo()); Assert.Equal(0, restored.Current.Value);
        Assert.False(restored.Undo());
        Assert.Equal(payload, Bytes(owner.Current)); // Restoring/editing never changed the original owner.
    }

    [Fact]
    public void Corrupt_foreign_duplicate_metadata_and_malformed_snapshots_fail_before_a_new_owner_is_published()
    {
        var owner = NewOwner();
        owner.Apply("First", DocumentOperationOrigin.User, state => { state.Value = 1; state.Revision++; });
        owner.Apply("Second", DocumentOperationOrigin.User, state => { state.Value = 2; state.Revision++; });
        var history = Capture(owner);
        var before = Bytes(owner.Current);
        Assert.Throws<InvalidDataException>(() => Restore(history with { CurrentSnapshotHash = new string('0', 64) }, owner.Current));
        Assert.Throws<InvalidDataException>(() => Restore(history with { ArtifactId = Guid.NewGuid() }, owner.Current));
        var frames = history.Undo.ToArray();
        frames[1] = frames[1] with { Operation = frames[0].Operation };
        Assert.Throws<InvalidDataException>(() => Restore(history with { Undo = frames }, owner.Current));
        frames = history.Undo.ToArray(); frames[0] = frames[0] with { PayloadBase64 = "not-base64" };
        Assert.Throws<InvalidDataException>(() => Restore(history with { Undo = frames }, owner.Current));
        Assert.Throws<InvalidDataException>(() => Restore(history with
        { LastOperation = history.LastOperation! with { Origin = (DocumentOperationOrigin)999 } }, owner.Current));
        Assert.Equal(before, Bytes(owner.Current));
        Assert.True(owner.CanUndo);
        Assert.Equal(2, owner.Current.Value);
    }

    [Fact]
    public void Existing_limit_discard_counts_survive_restore_and_legacy_frames_are_honest_imports()
    {
        var owner = NewOwner(limit: 2);
        for (var value = 1; value <= 4; value++)
        {
            var captured = value;
            owner.Apply("Edit " + value, DocumentOperationOrigin.User, state => { state.Value = captured; state.Revision++; });
        }
        var history = Capture(owner);
        Assert.Equal(2, history.DiscardedEarlierEntries);
        Assert.Equal(2, history.Undo.Count);
        var restored = Restore(history, owner.Current, limit: 2);
        Assert.Equal(2, restored.DiscardedEarlierHistoryEntries);
        Assert.True(restored.Undo()); Assert.Equal(3, restored.Current.Value);
        Assert.True(restored.Undo()); Assert.Equal(2, restored.Current.Value);
        Assert.False(restored.Undo());
        var legacy = history with { Undo = history.Undo.Select(frame => frame with { Operation = null }).ToArray(), LastOperation = null };
        var imported = Restore(legacy, owner.Current, limit: 2);
        Assert.Null(imported.LastOperation);
        var recaptured = Capture(imported);
        Assert.All(recaptured.Undo, frame =>
        {
            Assert.Equal(DocumentOperationOrigin.Import, frame.Operation!.Origin);
            Assert.Contains("metadata unavailable", frame.Operation.Name);
            Assert.DoesNotContain(history.Undo, original => original.Operation!.Id == frame.Operation.Id);
        });
        Assert.Equal(2, recaptured.DiscardedEarlierEntries);
    }

    private static DocumentMutationHistory<State> NewOwner(int limit = 100) => new(new State { ArtifactId = Guid.NewGuid() }, Clone, limit);
    private static byte[] Bytes(State value) => JsonSerializer.SerializeToUtf8Bytes(value);
    private static State Clone(State value) => JsonSerializer.Deserialize<State>(Bytes(value))!;
    private static ProductivitySnapshotHistory Capture(DocumentMutationHistory<State> owner) => owner.CaptureSnapshotHistory(
        "test.document", owner.Current.ArtifactId, owner.Current.Revision.ToString(), Bytes(owner.Current),
        state => (Bytes(state), state.Revision.ToString()));
    private static DocumentMutationHistory<State> Restore(ProductivitySnapshotHistory history, State current, int limit = 100) =>
        DocumentMutationHistory<State>.RestoreSnapshotHistory(history, "test.document", current.ArtifactId,
            current.Revision.ToString(), Bytes(current), payload =>
            {
                var state = JsonSerializer.Deserialize<State>(payload.Span) ?? throw new InvalidDataException("Missing test state.");
                if (state.ArtifactId == Guid.Empty || state.Revision < 0) throw new InvalidDataException("Invalid test state.");
                return (state, state.ArtifactId, state.Revision.ToString());
            }, Clone, limit);

    private sealed class State
    {
        public State() { }
        public Guid ArtifactId { get; set; }
        public long Revision { get; set; }
        public int Value { get; set; }
    }
}
