using System.Text;
using System.Text.Json;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureArtifactCodecTests
{
    [Fact]
    public void Mutable_caller_operation_graph_is_captured_once_before_validation_and_emission()
    {
        var operations = new ChangingOperations();
        var document = new PictureDocument { CanvasWidth = 30, CanvasHeight = 20, Revision = 1, Operations = operations };
        var envelope = new PictureArtifactEnvelope { BackingFileId = Guid.NewGuid(), Document = document };
        var decoded = PictureArtifactCodec.Deserialize(PictureArtifactCodec.Serialize(envelope));
        Assert.Equal(1, operations.Enumerations);
        Assert.Equal(new RotateOperation(1), Assert.Single(decoded.Document.Operations));
        Assert.Equal(document.DocumentId, decoded.Document.DocumentId);
    }

    private sealed class ChangingOperations : IReadOnlyList<PictureOperation>
    {
        public int Enumerations { get; private set; }
        public int Count => int.MaxValue;
        public PictureOperation this[int index] => throw new InvalidOperationException("Caller indexer must not be used");
        public IEnumerator<PictureOperation> GetEnumerator()
        {
            Enumerations++;
            yield return Enumerations == 1 ? new RotateOperation(1) : new ResizeOperation(-1, -1);
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void Portable_editable_envelope_keeps_source_and_backing_identity_distinct_across_transform_roundtrip()
    {
        var sourceId = Guid.NewGuid();
        var sourceRevision = Guid.NewGuid();
        var source = new PictureSourceAssetReference(sourceId, sourceRevision, new string('A', 64), 100, Guid.NewGuid());
        var document = PictureDocument.Create(20, 30, sourceId.ToString(), sourceRevision.ToString()).Rotate().Flip(true).Resize(40, 60);
        var envelope = new PictureArtifactEnvelope { BackingFileId = Guid.NewGuid(), Document = document, SourceAsset = source };
        var bytes = PictureArtifactCodec.Serialize(envelope);
        var restored = PictureArtifactCodec.Deserialize(bytes);
        Assert.Equal(envelope.BackingFileId, restored.BackingFileId);
        Assert.NotEqual(sourceId, restored.BackingFileId);
        Assert.Equal(document.DocumentId, restored.Document.DocumentId);
        Assert.Equal(source, restored.SourceAsset);
        Assert.Equal(document.Operations, restored.Document.Operations);
        Assert.Equal(3, restored.Document.Revision);
        Assert.Null(restored.Document.SourcePath);
    }

    [Fact]
    public void Wrong_source_revision_alias_backing_identity_machine_path_or_missing_identity_are_rejected()
    {
        var sourceId = Guid.NewGuid();
        var revision = Guid.NewGuid();
        var source = new PictureSourceAssetReference(sourceId, revision, new string('A', 64), 20, Guid.NewGuid());
        var document = PictureDocument.Create(10, 10, sourceId.ToString(), revision.ToString());
        var envelope = new PictureArtifactEnvelope { BackingFileId = Guid.NewGuid(), Document = document, SourceAsset = source };
        Assert.Throws<InvalidDataException>(() => PictureArtifactCodec.Serialize(envelope with { BackingFileId = sourceId }));
        Assert.Throws<InvalidDataException>(() => PictureArtifactCodec.Serialize(envelope with { SourceAsset = source with { RevisionId = Guid.NewGuid() } }));
        Assert.Throws<InvalidDataException>(() => PictureArtifactCodec.Serialize(envelope with { Document = PictureDocument.Create(10, 10, sourceId.ToString(), revision.ToString(), "/machine/path.png") }));
        var json = Encoding.UTF8.GetString(PictureArtifactCodec.Serialize(envelope));
        var missing = json.Replace($"\"documentId\":\"{document.DocumentId}\",", "", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => PictureArtifactCodec.Deserialize(Encoding.UTF8.GetBytes(missing)));
        Assert.Throws<NotSupportedException>(() => PictureArtifactCodec.Serialize(envelope with { SchemaVersion = 99 }));
    }
}
