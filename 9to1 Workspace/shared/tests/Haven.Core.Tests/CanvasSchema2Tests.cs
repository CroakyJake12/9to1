using System.Collections;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Application.Canvas;

namespace Haven.Core.Tests;

/// <summary>Canonical codec/session boundary tests. Constructed curves/callbacks are controlled
/// semantic fixtures, not genuine native curve/Files/Home permission proof.</summary>
public sealed class CanvasSchema2Tests
{
    [Fact]
    public void Retained_actual_schema1_native_artifact_preserves_identity_samples_and_native_state()
    {
        var bytes = File.ReadAllBytes(LegacyFixture());
        Assert.Equal("835e8cfea997ca120a34bd860eeaeb5bbcb29ab840d16dc89799e18e7030669d",
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        var artifact = CanvasArtifactCodec.Deserialize(bytes);
        Assert.Equal(1, artifact.SchemaVersion);
        Assert.All(artifact.Pages.SelectMany(p => p.Strokes), s => Assert.Null(s.PathGeometry));
        var canonical = CanvasArtifactCodec.Serialize(artifact);
        var reopened = CanvasArtifactCodec.Deserialize(canonical);
        Assert.Equal(artifact.ArtifactId, reopened.ArtifactId);
        Assert.Equal(artifact.RevisionId, reopened.RevisionId);
        Assert.Equal(artifact.PageOrder, reopened.PageOrder);
        Assert.Equal(JsonSerializer.Serialize(artifact.Pages), JsonSerializer.Serialize(reopened.Pages));
        Assert.Equal(JsonSerializer.Serialize(artifact.DocumentSettings), JsonSerializer.Serialize(reopened.DocumentSettings));
        Assert.Equal(canonical, CanvasArtifactCodec.Serialize(reopened));
        using var envelope = JsonDocument.Parse(canonical);
        Assert.Equal(1, envelope.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.DoesNotContain("pathGeometry", System.Text.Encoding.UTF8.GetString(canonical), StringComparison.Ordinal);
    }

    [Fact]
    public void Schema2_authoritative_path_roundtrip_retains_samples_as_original_provenance()
    {
        var artifact = Seed();
        var session = new CanvasArtifactSession(artifact);
        var stroke = Stroke(artifact.Pages[0].Layers[0].LayerId) with { PathGeometry = Path() };
        Assert.True(session.AddStructuredStroke(Request(session), artifact.Pages[0].PageId, stroke).IsSuccess);
        var snapshot = CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        Assert.Equal(2, snapshot.SchemaVersion);
        var retained = Assert.Single(snapshot.Pages[0].Strokes, s => s.StrokeId == stroke.StrokeId);
        Assert.Equal(stroke.Samples, retained.Samples);
        Assert.Equal(JsonSerializer.Serialize(stroke.PathGeometry), JsonSerializer.Serialize(retained.PathGeometry));
        var json = JsonNode.Parse(CanvasArtifactCodec.Serialize(snapshot))!.AsObject();
        json["schemaVersion"] = 1; json["artifact"]!["schemaVersion"] = 1;
        Assert.Throws<CanvasArtifactFormatException>(() => CanvasArtifactCodec.Deserialize(JsonSerializer.SerializeToUtf8Bytes(json)));
    }

    [Fact]
    public void Atomic_replace_has_one_history_frame_exact_replay_and_schema1_undo_after_reopen()
    {
        var original = Seed(); var page = original.Pages[0];
        var session = new CanvasArtifactSession(original);
        var first = page.Strokes[0] with { RevisionId = Guid.NewGuid(), PathGeometry = Path() };
        var fragment = first with { StrokeId = Guid.NewGuid(), RevisionId = Guid.NewGuid() };
        var request = Request(session); var calls = 0;
        CanvasDocumentSettings Prepare() { calls++; return new(); }
        Assert.True(session.ReplaceStructuredStrokes(request, page.PageId, [first, fragment], [page.Strokes[1].StrokeId],
            [first.StrokeId, fragment.StrokeId], Prepare).IsSuccess);
        Assert.Equal(1, calls);
        Assert.Single(session.GetArtifactSnapshot().SemanticHistory!.Undo);
        Assert.True(session.RenameArtifact(Request(session), "Later view mutation").IsSuccess);
        var current = CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());
        Assert.True(session.ReplaceStructuredStrokes(request, page.PageId, [fragment, first], [page.Strokes[1].StrokeId],
            [first.StrokeId, fragment.StrokeId], Prepare).IsSuccess);
        Assert.Equal(1, calls); Assert.Equal(current, CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        var reopened = Reopen(session);
        Assert.True(reopened.Undo(Request(reopened)).IsSuccess); // Later rename.
        Assert.True(reopened.Undo(Request(reopened)).IsSuccess); // Atomic replacement.
        Assert.Equal(1, reopened.GetArtifactSnapshot().SchemaVersion);
        Assert.Equal(original.ArtifactId, reopened.ArtifactId);
        Assert.Equal(page.StrokeOrder, reopened.GetArtifactSnapshot().Pages[0].StrokeOrder);
        Assert.All(reopened.GetArtifactSnapshot().Pages[0].Strokes, s => Assert.Null(s.PathGeometry));
        reopened = Reopen(reopened);
        Assert.True(reopened.Redo(Request(reopened)).IsSuccess);
        Assert.Equal(2, reopened.GetArtifactSnapshot().SchemaVersion);
        Assert.Equal(JsonSerializer.Serialize(first.PathGeometry), JsonSerializer.Serialize(reopened.GetArtifactSnapshot().Pages[0].Strokes[0].PathGeometry));
    }

    [Fact]
    public void Invalid_stale_noop_locked_and_throwing_candidates_do_not_mutate_or_prepare()
    {
        var seed = Seed(); var page = seed.Pages[0]; var session = new CanvasArtifactSession(seed);
        var before = CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()); var calls = 0;
        CanvasDocumentSettings Prepare() { calls++; return new(); }
        Assert.True(session.ReplaceStructuredStrokes(Request(session), page.PageId, [page.Strokes[0] with { RevisionId = Guid.NewGuid() }], [], page.StrokeOrder, Prepare).IsSuccess);
        Assert.Equal(0, calls); Assert.Equal(before, CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        var bad = page.Strokes[0] with { RevisionId = Guid.NewGuid(), PathGeometry = Path() with { Start = new(double.NaN, 0, .5) } };
        Assert.Equal(CanvasApiErrorCode.InvalidArgument, session.ReplaceStructuredStrokes(Request(session), page.PageId, [bad], [], page.StrokeOrder, Prepare).Error!.Code);
        var valid = page.Strokes[0] with { RevisionId = Guid.NewGuid(), PathGeometry = Path() };
        var stale = Request(session) with { BaseRevisionId = Guid.NewGuid() };
        Assert.Equal(CanvasApiErrorCode.RevisionConflict, session.ReplaceStructuredStrokes(stale, page.PageId, [valid], [], page.StrokeOrder, Prepare).Error!.Code);
        Assert.Equal(before, CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot())); Assert.Equal(0, calls);
        var request = Request(session);
        Assert.Throws<IOException>(() => session.ReplaceStructuredStrokes(request, page.PageId, [valid], [], page.StrokeOrder,
            () => { calls++; throw new IOException("Controlled donor preparation failure."); }));
        Assert.Equal(before, CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        Assert.True(session.ReplaceStructuredStrokes(request, page.PageId, [valid], [], page.StrokeOrder, Prepare).IsSuccess);
        Assert.Equal(2, calls);
        var lockedSeed = Seed(); var lockedPage = lockedSeed.Pages[0];
        lockedSeed.Pages[0] = lockedPage with { Layers = [lockedPage.Layers[0] with { IsLocked = true }] };
        var locked = new CanvasArtifactSession(lockedSeed); var lockedBefore = CanvasArtifactCodec.Serialize(locked.GetArtifactSnapshot());
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, locked.DeleteStructuredStrokes(Request(locked), lockedPage.PageId,
            [lockedPage.Strokes[0].StrokeId], Prepare).Error!.Code);
        Assert.Equal(2, calls); Assert.Equal(lockedBefore, CanvasArtifactCodec.Serialize(locked.GetArtifactSnapshot()));
    }

    [Fact]
    public void Batch_delete_replay_is_normalized_and_callback_cannot_reenter_receiver()
    {
        var seed = Seed(); var page = seed.Pages[0]; var session = new CanvasArtifactSession(seed);
        var request = Request(session); var calls = 0; CanvasApiError? nestedError = null;
        CanvasDocumentSettings Prepare()
        {
            calls++; nestedError = session.RenameArtifact(Request(session), "Injected nested mutation").Error;
            return new();
        }
        Assert.True(session.DeleteStructuredStrokes(request, page.PageId, page.StrokeOrder, Prepare).IsSuccess);
        Assert.Equal(CanvasApiErrorCode.InvalidArgument, nestedError!.Code);
        Assert.Equal(seed.DisplayName, session.GetArtifactSnapshot().DisplayName);
        Assert.Single(session.GetArtifactSnapshot().SemanticHistory!.Undo);
        var after = CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());
        Assert.True(session.DeleteStructuredStrokes(request, page.PageId, page.StrokeOrder.AsEnumerable().Reverse().ToArray(), Prepare).IsSuccess);
        Assert.Equal(1, calls); Assert.Equal(after, CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        Assert.Equal(CanvasApiErrorCode.OperationIdConflict, session.DeleteStructuredStrokes(request, page.PageId, [page.StrokeOrder[0]], Prepare).Error!.Code);
    }

    [Fact]
    public void Lying_collection_count_cannot_expand_batch_capture_or_invoke_callback()
    {
        var seed = Seed(); var page = seed.Pages[0]; var session = new CanvasArtifactSession(seed);
        var before = CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()); var calls = 0;
        var ids = new LyingList<Guid>(page.StrokeOrder[0], 4096);
        Assert.Equal(CanvasApiErrorCode.InvalidArgument, session.DeleteStructuredStrokes(Request(session), page.PageId, ids, () => { calls++; return new(); }).Error!.Code);
        Assert.Equal(1025, ids.Consumed);
        var strokes = new LyingList<CanvasInkStroke>(page.Strokes[0], 4096);
        Assert.Equal(CanvasApiErrorCode.InvalidArgument, session.ReplaceStructuredStrokes(Request(session), page.PageId, strokes, [], page.StrokeOrder,
            () => { calls++; return new(); }).Error!.Code);
        Assert.Equal(1025, strokes.Consumed);
        Assert.Equal(0, calls); Assert.Equal(before, CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
    }
    private static string LegacyFixture([CallerFilePath] string source = "") =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source)!, "Fixtures", "CanvasSchema1Native13.9to1c");
    private static CanvasArtifact Seed()
    {
        var artifact = CanvasArtifact.Create("Controlled schema fixture"); artifact.SchemaVersion = 1;
        var page = artifact.Pages[0]; var strokes = new List<CanvasInkStroke> { Stroke(page.Layers[0].LayerId), Stroke(page.Layers[0].LayerId) };
        artifact.Pages[0] = page with { Strokes = strokes, StrokeOrder = strokes.Select(s => s.StrokeId).ToList() };
        return artifact;
    }
    private static CanvasInkStroke Stroke(Guid layer) => new() { LayerId = layer, Samples = [new(1, 2, .5, 3, 4, 7), new(10, 20, .75, 5, 6, 9)] };
    private static CanvasStrokePathGeometry Path() => new() { Start = new(1, 2, .5), Segments = [
        new() { Kind = CanvasStrokePathSegmentKind.Line, End = new(2, 3, .4) },
        new() { Kind = CanvasStrokePathSegmentKind.Quadratic, End = new(4, 5, .6), Control1 = new(8, 9) },
        new() { Kind = CanvasStrokePathSegmentKind.Cubic, End = new(6, 7, .8), Control1 = new(10, 11), Control2 = new(12, 13) }] };
    private static CanvasMutationRequest Request(CanvasArtifactSession session) => new(session.CurrentRevisionId, Guid.NewGuid(), new("controlled-schema-fixture", "Fixture"));
    private static CanvasArtifactSession Reopen(CanvasArtifactSession session) => new(CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot())));
    private sealed class LyingList<T>(T value, int actualCount) : IReadOnlyList<T>
    {
        public int Count => 1; public T this[int index] => value; public int Consumed { get; private set; }
        public IEnumerator<T> GetEnumerator() { for (var i = 0; i < actualCount; i++) { Consumed++; yield return value; } }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
