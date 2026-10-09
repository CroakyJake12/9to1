using System.Security.Cryptography;

namespace Haven.Application;

public sealed partial class DocumentMutationHistory<T> where T : class
{
    private long _discardedEarlierHistoryEntries;

    /// <summary>Lower bound of entries removed by the existing history limit or
    /// durable snapshot budget. It is not an original action count.</summary>
    public long DiscardedEarlierHistoryEntries => _discardedEarlierHistoryEntries;

    /// <summary>Exports the SAME engine's ordered before/after snapshots in the
    /// canonical app-neutral envelope. The owning format supplies validated,
    /// history-free payloads; this method supplies hashes and actual metadata.</summary>
    public ProductivitySnapshotHistory CaptureSnapshotHistory(string ownerFormat, Guid artifactId,
        string currentRevision, ReadOnlyMemory<byte> currentSnapshot,
        Func<T, (ReadOnlyMemory<byte> Payload, string Revision)> captureOwnerSnapshot)
    {
        ArgumentNullException.ThrowIfNull(captureOwnerSnapshot);
        if (string.IsNullOrWhiteSpace(ownerFormat) || ownerFormat.Length > 128 || artifactId == Guid.Empty ||
            string.IsNullOrWhiteSpace(currentRevision) || currentRevision.Length > 128 || currentSnapshot.IsEmpty ||
            currentSnapshot.Length > ProductivitySnapshotHistory.MaximumPayloadBytes)
            throw new InvalidDataException("The owning current history binding is invalid.");
        var originalCurrent = currentSnapshot.ToArray();
        var originalUndo = _undo.ToArray(); var originalRedo = _redo.ToArray();
        var originalLastOperation = LastOperation;
        var originalDiscarded = _discardedEarlierHistoryEntries;
        var undo = new List<ProductivityHistorySnapshot>();
        var redo = new List<ProductivityHistorySnapshot>();
        long bytes = 0, omitted = 0;
        void Capture(IReadOnlyList<Entry> entries, List<ProductivityHistorySnapshot> destination, bool before)
        {
            // Serialize nearest-current entries first, bounded before retention.
            // The original engine remains untouched by an export budget.
            for (var index = entries.Count - 1; index >= 0; index--)
            {
                if (undo.Count + redo.Count == ProductivitySnapshotHistory.MaximumEntries)
                { omitted = SaturatingAdd(omitted, index + 1L); break; }
                var entry = entries[index];
                var supplied = captureOwnerSnapshot(_clone(before ? entry.Before : entry.After));
                if (supplied.Payload.IsEmpty || supplied.Payload.Length > ProductivitySnapshotHistory.MaximumPayloadBytes)
                    throw new InvalidDataException("An owning history snapshot exceeds the supported payload budget.");
                if (bytes + supplied.Payload.Length > ProductivitySnapshotHistory.MaximumPayloadBytes)
                { omitted = SaturatingAdd(omitted, index + 1L); break; }
                destination.Add(ProductivitySnapshotHistory.Capture(supplied.Payload.Span, supplied.Revision)
                    with { Operation = entry.Metadata });
                bytes += supplied.Payload.Length;
            }
            destination.Reverse();
        }
        Capture(originalUndo, undo, before: true);
        Capture(originalRedo, redo, before: false);
        return new ProductivitySnapshotHistory
        {
            OwnerFormat = ownerFormat, ArtifactId = artifactId, CurrentRevision = currentRevision,
            CurrentSnapshotHash = Convert.ToHexString(SHA256.HashData(originalCurrent)),
            Undo = Array.AsReadOnly(undo.ToArray()), Redo = Array.AsReadOnly(redo.ToArray()),
            DiscardedEarlierEntries = SaturatingAdd(originalDiscarded, omitted),
            LastOperation = originalLastOperation
        };
    }

    /// <summary>Creates a new instance of the SAME mutation engine from a fully
    /// validated canonical envelope. Original metadata/order is retained;
    /// legacy frames with no metadata are explicitly identified as imports.
    /// No existing live owner is overwritten on a failed restore.</summary>
    public static DocumentMutationHistory<T> RestoreSnapshotHistory(ProductivitySnapshotHistory history,
        string ownerFormat, Guid artifactId, string currentRevision, ReadOnlyMemory<byte> currentSnapshot,
        Func<ReadOnlyMemory<byte>, (T State, Guid ArtifactId, string Revision)> decodeOwnerSnapshot,
        Func<T, T> clone, int limit = 100)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(decodeOwnerSnapshot);
        ArgumentNullException.ThrowIfNull(clone);
        IReadOnlyList<ProductivityHistorySnapshot> Detach(IReadOnlyList<ProductivityHistorySnapshot>? frames)
        {
            if (frames is null || frames.Count > ProductivitySnapshotHistory.MaximumEntries)
                throw new InvalidDataException("History entries are missing or exceed their retention bound.");
            var count = frames.Count;
            var detached = frames.Take(ProductivitySnapshotHistory.MaximumEntries + 1).ToArray();
            if (detached.Length != count || detached.Length > ProductivitySnapshotHistory.MaximumEntries)
                throw new InvalidDataException("History entry enumeration disagrees with its retention bound.");
            return Array.AsReadOnly(detached);
        }
        history = history with { Undo = Detach(history.Undo), Redo = Detach(history.Redo) };
        if (currentSnapshot.IsEmpty || currentSnapshot.Length > ProductivitySnapshotHistory.MaximumPayloadBytes)
            throw new InvalidDataException("The current owning snapshot exceeds its supported history payload bound.");
        var originalCurrent = currentSnapshot.ToArray();
        var current = decodeOwnerSnapshot(originalCurrent);
        if (current.State is null || current.ArtifactId != artifactId || current.Revision != currentRevision)
            throw new InvalidDataException("The decoded current snapshot does not match its owning identity and revision.");
        var decoded = new List<T>();
        history.Validate(ownerFormat, artifactId, currentRevision, originalCurrent, payload =>
        {
            var frame = decodeOwnerSnapshot(payload);
            if (frame.State is null) throw new InvalidDataException("A retained history snapshot has no owning state.");
            decoded.Add(clone(frame.State));
            return (frame.ArtifactId, frame.Revision);
        });
        var result = new DocumentMutationHistory<T>(clone(current.State), clone, limit);
        DocumentOperationMetadata Metadata(ProductivityHistorySnapshot frame) => frame.Operation ??
            new(Guid.NewGuid(), "Retained edit (original metadata unavailable)", DocumentOperationOrigin.Import, DateTimeOffset.UtcNow);
        for (var index = 0; index < history.Undo.Count; index++)
            result._undo.Add(new(Metadata(history.Undo[index]), clone(decoded[index]),
                clone(index + 1 < history.Undo.Count ? decoded[index + 1] : current.State)));
        var redoOffset = history.Undo.Count;
        for (var index = 0; index < history.Redo.Count; index++)
            result._redo.Add(new(Metadata(history.Redo[index]),
                clone(index + 1 < history.Redo.Count ? decoded[redoOffset + index + 1] : current.State),
                clone(decoded[redoOffset + index])));
        result._discardedEarlierHistoryEntries = history.DiscardedEarlierEntries;
        while (result._undo.Count + result._redo.Count > result._limit)
        {
            var entries = result._undo.Count > 0 ? result._undo : result._redo;
            entries.RemoveAt(0);
            result._discardedEarlierHistoryEntries = SaturatingAdd(result._discardedEarlierHistoryEntries, 1);
        }
        result.LastOperation = history.LastOperation;
        return result;
    }

    private static long SaturatingAdd(long value, long additional) => value > long.MaxValue - additional ? long.MaxValue : value + additional;
}
