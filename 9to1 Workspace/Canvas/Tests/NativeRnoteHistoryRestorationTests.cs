using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class NativeRnoteHistoryRestorationTests
{
    [Fact]
    public void Successful_history_replay_keeps_current_native_and_canonical_state()
    {
        using var live = CanvasRnoteDocument.Create();
        live.DrawStroke([new(10, 20, .2), new(50, 60, .8)], live.Snapshot.RevisionId);
        var request = new CanvasMutationRequest(live.Snapshot.RevisionId, Guid.NewGuid(), new("history-owner", "History owner"));
        live.Undo(request);
        var before = live.Serialize();
        var donor = live.ExportRnote();
        live.Undo(request);
        Assert.Equal(before, live.Serialize());
        Assert.Equal(donor, live.ExportRnote());
    }

    [Fact]
    public void Invalid_donor_history_preserves_live_canonical_content_donor_and_history_for_retry()
    {
        using var original = CanvasRnoteDocument.Create();
        original.DrawStroke([new(10, 20, .2), new(50, 60, .8)], original.Snapshot.RevisionId);
        var artifact = original.Snapshot;
        var history = Assert.IsType<ProductivitySnapshotHistory>(artifact.SemanticHistory);
        var frame = Assert.Single(history.Undo);
        var prior = CanvasArtifactCodec.Deserialize(Convert.FromBase64String(frame.PayloadBase64));
        var state = JsonNode.Parse(prior.DocumentSettings.Properties["9to1.Canvas.RnoteState"].GetRawText())!.AsObject();
        byte[] malformed;
        using (var bytes = new MemoryStream())
        {
            using (var gzip = new GZipStream(bytes, CompressionLevel.Fastest, leaveOpen: true))
                gzip.Write("this is not a Rnote document"u8);
            malformed = bytes.ToArray();
        }
        state["PayloadBase64"] = Convert.ToBase64String(malformed);
        state["Sha256"] = Convert.ToHexString(SHA256.HashData(malformed));
        prior.DocumentSettings.Properties["9to1.Canvas.RnoteState"] = JsonSerializer.SerializeToElement(state);
        artifact.SemanticHistory = history with
        {
            Undo = [ProductivitySnapshotHistory.Capture(CanvasArtifactCodec.Serialize(prior), prior.RevisionId.ToString("D"))]
        };
        using var live = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(artifact));
        var before = live.Serialize();
        var donor = live.ExportRnote();
        var request = new CanvasMutationRequest(live.Snapshot.RevisionId, Guid.NewGuid(), new("history-owner", "History owner"));
        Assert.Throws<InvalidOperationException>(() => live.Undo(request));
        Assert.Equal(before, live.Serialize());
        Assert.Equal(donor, live.ExportRnote());
        Assert.Throws<InvalidOperationException>(() => live.Undo(request));
        Assert.Equal(before, live.Serialize());
        Assert.Equal(donor, live.ExportRnote());
    }
}
