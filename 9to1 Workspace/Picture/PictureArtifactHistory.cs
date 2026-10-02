using System.Globalization;
using System.Security.Cryptography;
using Haven.Application;

namespace HavenOS.Images;

/// <summary>Picture's owner adapter for the common history envelope; never an authority or a private history format.</summary>
public static class PictureArtifactHistory
{
    internal static void Validate(PictureArtifactEnvelope current)
    {
        current.SemanticHistory?.Validate(PictureArtifactEnvelope.FormatId, current.Document.DocumentId,
            Revision(current), PictureArtifactCodec.SerializeSnapshot(current), bytes =>
            {
                var frame = PictureArtifactCodec.DeserializeSnapshot(bytes.Span);
                CheckBindings(current, frame);
                return (frame.Document.DocumentId, Revision(frame));
            });
    }

    public static PictureArtifactEnvelope Apply(PictureArtifactEnvelope current, PictureDocument edited)
    {
        current = PictureArtifactCodec.Deserialize(PictureArtifactCodec.Serialize(current));
        edited = PictureDocument.Deserialize(edited.Serialize());
        var candidate = current with { Document = edited, SemanticHistory = null };
        CheckBindings(current, candidate);
        if (edited.Revision == current.Document.Revision)
        {
            if (!PictureArtifactCodec.SerializeSnapshot(current).AsSpan().SequenceEqual(PictureArtifactCodec.SerializeSnapshot(candidate)))
                throw new InvalidDataException("A Picture revision cannot identify different content.");
            return current;
        }
        if (edited.Revision != checked(current.Document.Revision + 1))
            throw new InvalidDataException("A Picture edit must advance exactly one owning revision.");
        var undo = current.SemanticHistory?.Undo.ToList() ?? [];
        undo.Add(ProductivitySnapshotHistory.Capture(PictureArtifactCodec.SerializeSnapshot(current), Revision(current)));
        return Attach(candidate, undo, [], current.SemanticHistory?.DiscardedEarlierEntries ?? 0);
    }

    public static PictureArtifactEnvelope Restore(PictureArtifactEnvelope current, bool undo)
    {
        current = PictureArtifactCodec.Deserialize(PictureArtifactCodec.Serialize(current));
        var history = current.SemanticHistory;
        var source = (undo ? history?.Undo : history?.Redo)?.ToList() ?? [];
        if (source.Count == 0) throw new InvalidOperationException("Picture history is unavailable at the retained boundary.");
        var opposite = (undo ? history!.Redo : history!.Undo).ToList();
        var frame = PictureArtifactCodec.DeserializeSnapshot(Convert.FromBase64String(source[^1].PayloadBase64));
        CheckBindings(current, frame);
        var d = frame.Document;
        var restored = frame with { Document = new PictureDocument
        {
            DocumentId = d.DocumentId, SchemaVersion = d.SchemaVersion, DisplayName = d.DisplayName,
            FileId = d.FileId, SourcePath = d.SourcePath, SourceRevision = d.SourceRevision,
            CanvasWidth = d.CanvasWidth, CanvasHeight = d.CanvasHeight, Operations = d.Operations,
            Revision = checked(current.Document.Revision + 1)
        }, SemanticHistory = null };
        source.RemoveAt(source.Count - 1);
        opposite.Add(ProductivitySnapshotHistory.Capture(PictureArtifactCodec.SerializeSnapshot(current), Revision(current)));
        return undo ? Attach(restored, source, opposite, history!.DiscardedEarlierEntries)
                    : Attach(restored, opposite, source, history!.DiscardedEarlierEntries);
    }

    private static PictureArtifactEnvelope Attach(PictureArtifactEnvelope current, List<ProductivityHistorySnapshot> undo,
        List<ProductivityHistorySnapshot> redo, long discarded)
    {
        long bytes = undo.Concat(redo).Sum(frame => (long)Convert.FromBase64String(frame.PayloadBase64).Length);
        while (undo.Count + redo.Count > ProductivitySnapshotHistory.MaximumEntries || bytes > ProductivitySnapshotHistory.MaximumPayloadBytes)
        {
            var stack = undo.Count != 0 ? undo : redo;
            bytes -= Convert.FromBase64String(stack[0].PayloadBase64).Length;
            stack.RemoveAt(0);
            if (discarded != long.MaxValue) discarded++;
        }
        return current with { SemanticHistory = new ProductivitySnapshotHistory
        {
            OwnerFormat = PictureArtifactEnvelope.FormatId, ArtifactId = current.Document.DocumentId,
            CurrentRevision = Revision(current),
            CurrentSnapshotHash = Convert.ToHexString(SHA256.HashData(PictureArtifactCodec.SerializeSnapshot(current))),
            Undo = Array.AsReadOnly(undo.ToArray()), Redo = Array.AsReadOnly(redo.ToArray()),
            DiscardedEarlierEntries = discarded
        } };
    }

    private static string Revision(PictureArtifactEnvelope value) => value.Document.Revision.ToString(CultureInfo.InvariantCulture);
    private static void CheckBindings(PictureArtifactEnvelope current, PictureArtifactEnvelope frame)
    {
        if (frame.BackingFileId != current.BackingFileId || frame.Document.DocumentId != current.Document.DocumentId ||
            frame.SourceAsset != current.SourceAsset || frame.Document.FileId != current.Document.FileId ||
            frame.Document.SourceRevision != current.Document.SourceRevision || frame.Document.SourcePath != current.Document.SourcePath)
            throw new InvalidDataException("Picture history cannot change its backing document or primary source asset.");
    }
}
