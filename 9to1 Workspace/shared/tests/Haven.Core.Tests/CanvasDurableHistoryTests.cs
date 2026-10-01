using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;

namespace Haven.Core.Tests;

/// <summary>Actual canonical session/codec restart workflow. Files/Home publication and native donor
/// restoration require separate owning integration proof; this suite supplies no resource grant.</summary>
public sealed class CanvasDurableHistoryTests
{
    [Fact]
    public void Canonical_codec_reopen_preserves_undo_and_redo_with_new_revision()
    {
        var original = CanvasArtifact.Create("Original");
        var session = new CanvasArtifactSession(original);
        Assert.True(session.RenameArtifact(Request(session), "Changed").IsSuccess);
        var changedRevision = session.CurrentRevisionId;
        session = Reopen(session);
        Assert.True(session.Undo(Request(session)).IsSuccess);
        Assert.Equal("Original", session.GetArtifactSnapshot().DisplayName);
        Assert.Equal(original.ArtifactId, session.ArtifactId);
        Assert.NotEqual(original.RevisionId, session.CurrentRevisionId);
        Assert.NotEqual(changedRevision, session.CurrentRevisionId);
        session = Reopen(session);
        Assert.True(session.Redo(Request(session)).IsSuccess);
        Assert.Equal("Changed", session.GetArtifactSnapshot().DisplayName);
        Assert.NotEqual(changedRevision, session.CurrentRevisionId);
    }

    [Fact]
    public void Stale_history_request_cannot_change_current_document_or_retained_history()
    {
        var session = new CanvasArtifactSession(CanvasArtifact.Create());
        var stale = Request(session);
        Assert.True(session.RenameArtifact(Request(session), "Changed").IsSuccess);
        session = Reopen(session);
        var before = CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());
        Assert.Equal(CanvasApiErrorCode.RevisionConflict, session.Undo(stale).Error!.Code);
        Assert.Equal(before, CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        Assert.True(session.Undo(Request(session)).IsSuccess);
    }

    [Fact]
    public void Owner_preparation_failure_stale_request_and_replay_do_not_mutate_or_repeat_donor_preparation()
    {
        var session = new CanvasArtifactSession(CanvasArtifact.Create("Original"));
        var stale = Request(session);
        Assert.True(session.RenameArtifact(Request(session), "Changed").IsSuccess);
        session = Reopen(session);
        var before = CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());
        var preparations = 0;
        Assert.Equal(CanvasApiErrorCode.RevisionConflict, session.Undo(stale, _ => preparations++).Error!.Code);
        var request = Request(session);
        Assert.Throws<InvalidDataException>(() => session.Undo(request, _ =>
        { preparations++; throw new InvalidDataException("Controlled owner snapshot rejection."); }));
        Assert.Equal(before, CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        Assert.True(session.Undo(request, proposed =>
        {
            preparations++;
            Assert.Equal("Original", proposed.DisplayName);
            Assert.Equal(session.ArtifactId, proposed.ArtifactId);
            proposed.DisplayName = "Detached caller mutation";
        }).IsSuccess);
        Assert.Equal("Original", session.GetArtifactSnapshot().DisplayName);
        var committed = CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());
        Assert.True(session.Undo(request, _ => preparations++).IsSuccess);
        Assert.Equal(2, preparations);
        Assert.Equal(committed, CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("current-revision")]
    [InlineData("owner")]
    [InlineData("schema")]
    [InlineData("nested")]
    [InlineData("foreign-snapshot")]
    public void Corrupt_unknown_or_cross_document_history_is_rejected_without_reinterpreting(string corruption)
    {
        var session = new CanvasArtifactSession(CanvasArtifact.Create());
        Assert.True(session.RenameArtifact(Request(session), "Changed").IsSuccess);
        var snapshot = session.GetArtifactSnapshot();
        var json = JsonNode.Parse(CanvasArtifactCodec.Serialize(snapshot))!.AsObject();
        var history = json["semanticHistory"]!.AsObject();
        switch (corruption)
        {
            case "hash": history["currentSnapshotHash"] = "00"; break;
            case "current-revision": history["currentRevision"] = Guid.NewGuid().ToString("D"); break;
            case "owner": history["ownerFormat"] = "9to1.Picture"; break;
            case "schema": history["schemaVersion"] = 999; break;
            case "nested":
            case "foreign-snapshot":
                var bytes = corruption == "nested" ? CanvasArtifactCodec.Serialize(snapshot) : CanvasArtifactCodec.Serialize(CanvasArtifact.Create());
                var artifact = corruption == "nested" ? snapshot : CanvasArtifactCodec.Deserialize(bytes);
                var frame = ProductivitySnapshotHistory.Capture(bytes, artifact.RevisionId.ToString("D"));
                history["undo"] = JsonSerializer.SerializeToNode(new[] { frame }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                break;
        }
        Assert.Throws<CanvasArtifactFormatException>(() => CanvasArtifactCodec.Deserialize(JsonSerializer.SerializeToUtf8Bytes(json)));
    }

    [Fact]
    public void Retention_lower_bound_saturates_without_overflow_or_partial_history_mutation()
    {
        var session = new CanvasArtifactSession(CanvasArtifact.Create());
        for (var index = 0; index < ProductivitySnapshotHistory.MaximumEntries; index++)
            Assert.True(session.RenameArtifact(Request(session), $"Revision {index}").IsSuccess);
        var snapshot = session.GetArtifactSnapshot();
        snapshot.SemanticHistory = snapshot.SemanticHistory! with { DiscardedEarlierEntries = long.MaxValue };
        session = new(CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(snapshot)));
        Assert.True(session.RenameArtifact(Request(session), "Saturated counter mutation").IsSuccess);
        session = Reopen(session);
        Assert.Equal(long.MaxValue, session.GetArtifactSnapshot().SemanticHistory!.DiscardedEarlierEntries);
        Assert.Equal(ProductivitySnapshotHistory.MaximumEntries, session.GetArtifactSnapshot().SemanticHistory!.Undo.Count);
        Assert.True(session.Undo(Request(session)).IsSuccess);
        Assert.Equal("Revision 127", session.GetArtifactSnapshot().DisplayName);
        Assert.True(Reopen(session).Redo(Request(session)).IsSuccess);
    }

    [Fact]
    public void Declared_retention_boundary_survives_reopen_and_missing_legacy_history_stays_unavailable()
    {
        var session = new CanvasArtifactSession(CanvasArtifact.Create());
        for (var index = 0; index < 130; index++)
            Assert.True(session.RenameArtifact(Request(session), $"Revision {index}").IsSuccess);
        var history = Reopen(session).GetArtifactSnapshot().SemanticHistory!;
        Assert.Equal(ProductivitySnapshotHistory.MaximumEntries, history.Undo.Count);
        Assert.Equal(2, history.DiscardedEarlierEntries);
        var legacy = new CanvasArtifactSession(CanvasArtifact.Create());
        Assert.Equal(CanvasApiErrorCode.HistoryUnavailable, legacy.Undo(Request(legacy)).Error!.Code);
    }

    private static CanvasArtifactSession Reopen(CanvasArtifactSession session) =>
        new(CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot())));
    private static CanvasMutationRequest Request(CanvasArtifactSession session) =>
        new(session.CurrentRevisionId, Guid.NewGuid(), new("controlled-session-fixture", "Fixture"));
}
