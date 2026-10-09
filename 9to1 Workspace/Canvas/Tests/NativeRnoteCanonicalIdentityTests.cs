using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class NativeRnoteCanonicalIdentityTests
{
    [Fact]
    public void Canonical_selected_identity_survives_atomic_draw_replay_restart_and_history()
    {
        using var document = CanvasRnoteDocument.Create();
        var actor = new CanvasActorContext("authorized-native-test", "Native test");
        var firstRequest = new CanvasMutationRequest(document.Snapshot.RevisionId, Guid.NewGuid(), actor);
        var firstSamples = new RnotePointerSample[] { new(10, 20, 0.2), new(40, 60, 0.7) };
        var first = document.DrawStroke(firstSamples, firstRequest);
        var firstBytes = document.ExportCanonicalStrokeSelection([first], document.Snapshot.RevisionId);
        var second = document.DrawStroke([new(900, 950, 0.3), new(930, 980, 0.8)],
            new CanvasMutationRequest(document.Snapshot.RevisionId, Guid.NewGuid(), actor));
        var currentRevision = document.Snapshot.RevisionId;
        var secondBytes = document.ExportCanonicalStrokeSelection([second],currentRevision);
        Assert.Equal(first, document.DrawStroke(firstSamples, firstRequest)); // canonical idempotent replay
        Assert.Equal(currentRevision, document.Snapshot.RevisionId);
        Assert.Equal(2, document.Snapshot.Pages[0].Strokes.Count);
        using var reopened = CanvasRnoteDocument.Open(document.Serialize());
        var afterRestart = reopened.ExportCanonicalStrokeSelection([first], currentRevision);
        AssertSameStroke(firstBytes, afterRestart);
        using var nativeFirst = RnoteCanvasEngine.Open(afterRestart);
        Assert.Single(nativeFirst.ReadStrokeKeys());
        Assert.Throws<InvalidOperationException>(() => reopened.ExportCanonicalStrokeSelection([first], firstRequest.BaseRevisionId));
        var before = reopened.Serialize();
        Assert.Throws<InvalidOperationException>(() => reopened.ExportCanonicalStrokeSelection([Guid.NewGuid()], currentRevision));
        Assert.Throws<ArgumentException>(() => reopened.ExportCanonicalStrokeSelection([first, first], currentRevision));
        Assert.Equal(before, reopened.Serialize());
        // The common canonical history envelope restores data frames across restart.
        // Native keys and exact editable stroke data must survive owning Undo/Redo.
        reopened.Undo(new(currentRevision, Guid.NewGuid(), actor));
        Assert.NotEqual(currentRevision, reopened.Snapshot.RevisionId);
        Assert.Equal(first, Assert.Single(reopened.Snapshot.Pages[0].Strokes).StrokeId);
        AssertSameStroke(firstBytes, reopened.ExportCanonicalStrokeSelection([first], reopened.Snapshot.RevisionId));
        Assert.Throws<InvalidOperationException>(() => reopened.ExportCanonicalStrokeSelection([second], reopened.Snapshot.RevisionId));
        reopened.Redo(new(reopened.Snapshot.RevisionId, Guid.NewGuid(), actor));
        Assert.Equal(new[] { first, second }.Order(), reopened.Snapshot.Pages[0].Strokes.Select(stroke => stroke.StrokeId).Order());
        AssertSameStroke(firstBytes, reopened.ExportCanonicalStrokeSelection([first], reopened.Snapshot.RevisionId));
        AssertSameStroke(secondBytes, reopened.ExportCanonicalStrokeSelection([second], reopened.Snapshot.RevisionId));
        using var reopenedAgain = CanvasRnoteDocument.Open(reopened.Serialize());
        Assert.Equal(reopened.Snapshot.RevisionId, reopenedAgain.Snapshot.RevisionId);
        Assert.Equal(new[] { first, second }.Order(), reopenedAgain.Snapshot.Pages[0].Strokes.Select(stroke => stroke.StrokeId).Order());
        AssertSameStroke(secondBytes,reopenedAgain.ExportCanonicalStrokeSelection([second],reopenedAgain.Snapshot.RevisionId));
        document.Undo(new(currentRevision, Guid.NewGuid(), actor));
        Assert.Single(document.Snapshot.Pages[0].Strokes);
        AssertSameStroke(firstBytes, document.ExportCanonicalStrokeSelection([first], document.Snapshot.RevisionId));
        Assert.Throws<InvalidOperationException>(() => document.ExportCanonicalStrokeSelection([second], document.Snapshot.RevisionId));
        document.Redo(new(document.Snapshot.RevisionId, Guid.NewGuid(), actor));
        var both = document.ExportCanonicalStrokeSelection([first, second], document.Snapshot.RevisionId);
        using var nativeBoth = RnoteCanvasEngine.Open(both);
        Assert.Equal(2, nativeBoth.ReadStrokeKeys().Length);
    }

    [Fact]
    public void Unknown_or_duplicate_persisted_bindings_are_rejected_and_legacy_unbound_content_never_exports_whole_document()
    {
        using var document = CanvasRnoteDocument.Create();
        var first = document.DrawStroke([new(10, 20, 0.2), new(40, 60, 0.7)], document.Snapshot.RevisionId);
        var second = document.DrawStroke([new(100, 120, 0.2), new(140, 160, 0.7)], document.Snapshot.RevisionId);
        var foreign = document.Snapshot;
        var state = State(foreign);
        var bindings = state["NativeStrokeKeys"]!.AsObject();
        bindings[Guid.NewGuid().ToString()] = bindings[first.ToString()]!.DeepClone();
        SaveState(foreign, state);
        // Retained history is content-bound: malformed current native state is
        // refused by the canonical codec before native materialization.
        Assert.Throws<CanvasArtifactFormatException>(() => CanvasArtifactCodec.Serialize(foreign));
        foreign.SemanticHistory = null; // Detached history-free fixture isolates the native binding boundary.
        Assert.Throws<InvalidDataException>(() => CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(foreign)));
        var duplicate = document.Snapshot;
        state = State(duplicate);
        state["NativeStrokeKeys"]![second.ToString()] = state["NativeStrokeKeys"]![first.ToString()]!.DeepClone();
        SaveState(duplicate, state);
        Assert.Throws<CanvasArtifactFormatException>(() => CanvasArtifactCodec.Serialize(duplicate));
        duplicate.SemanticHistory = null; // A separate native boundary fixture, not a history bypass in production.
        Assert.Throws<InvalidDataException>(() => CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(duplicate)));
        var legacy = document.Snapshot;
        state = State(legacy);
        state.Remove("NativeStrokeKeys");
        SaveState(legacy, state);
        Assert.Throws<CanvasArtifactFormatException>(() => CanvasArtifactCodec.Serialize(legacy));
        legacy.SemanticHistory = null; // Model the actual older history-free envelope with no key bindings.
        using var unbound = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(legacy));
        Assert.Throws<NotSupportedException>(() => unbound.ExportCanonicalStrokeSelection([first], unbound.Snapshot.RevisionId));
        Assert.Equal(2, unbound.Snapshot.Pages[0].Strokes.Count);
        var newStroke = unbound.DrawStroke([new(200, 220, 0.2), new(240, 260, 0.7)], unbound.Snapshot.RevisionId);
        using var newOnly = RnoteCanvasEngine.Open(unbound.ExportCanonicalStrokeSelection([newStroke], unbound.Snapshot.RevisionId));
        Assert.Single(newOnly.ReadStrokeKeys());
        Assert.Throws<NotSupportedException>(() => unbound.ExportCanonicalStrokeSelection([first, newStroke], unbound.Snapshot.RevisionId));
    }

    private static JsonObject State(CanvasArtifact artifact) => JsonNode.Parse(artifact.DocumentSettings.Properties["9to1.Canvas.RnoteState"].GetRawText())!.AsObject();
    private static void SaveState(CanvasArtifact artifact, JsonObject state) => artifact.DocumentSettings.Properties["9to1.Canvas.RnoteState"] = JsonSerializer.SerializeToElement(state);
    private static JsonDocument NativeJson(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
    private static void AssertSameStroke(byte[] expected, byte[] actual)
    {
        using var first = NativeJson(expected);
        using var second = NativeJson(actual);
        var a = first.RootElement.GetProperty("data").GetProperty("engine_snapshot").GetProperty("stroke_components")
            .EnumerateArray().Select(slot => slot.GetProperty("value")).Where(value => value.ValueKind != JsonValueKind.Null).ToArray();
        var b = second.RootElement.GetProperty("data").GetProperty("engine_snapshot").GetProperty("stroke_components")
            .EnumerateArray().Select(slot => slot.GetProperty("value")).Where(value => value.ValueKind != JsonValueKind.Null).ToArray();
        Assert.True(JsonElement.DeepEquals(Assert.Single(a), Assert.Single(b)));
        // Removing another selected-out slot retains donor allocation history;
        // compare actual entities and persisted keys, never empty-slot capacity.
        using var nativeFirst = RnoteCanvasEngine.Open(expected);
        using var nativeSecond = RnoteCanvasEngine.Open(actual);
        Assert.Equal(nativeFirst.ReadStrokeKeys().ToArray(), nativeSecond.ReadStrokeKeys().ToArray());
    }
}
