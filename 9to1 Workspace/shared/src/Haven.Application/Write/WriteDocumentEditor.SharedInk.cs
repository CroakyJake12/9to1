using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed record WriteSharedInkSnapshot(WriteDocumentEditor Owner, NotesDocument Document, NotesBlock Block, long Version,
    long Epoch, Guid? Selection, IReadOnlyList<JsonElement> Bundles);

public sealed partial class WriteDocumentEditor
{
    private bool _trackingSharedInk;
    private long _sharedInkEpoch;

    private void TrackSharedInk()
    {
        if (_trackingSharedInk) return;
        _trackingSharedInk = true;
        Changed += (_, _) => { checked { ++_sharedInkEpoch; } };
    }

    /// <summary>Install mutation epochs before presentation subscribers so a refresh observes
    /// the new unsaved owner generation, including ordinary edits, undo and redo.</summary>
    public void InitializeSharedInkTracking() => TrackSharedInk();

    /// <summary>Creates an empty owner-backed drawing through the normal undo history.</summary>
    public NotesBlock InsertSharedInk()
    {
        TrackSharedInk();
        var page = PageForSelection();
        var block = new NotesBlock { Kind = NotesBlockKind.Canvas,
            Canvas = new NotesCanvasData { Width = 960, Height = 540, Infinite = false },
            SharedProductivityBundles = [] };
        Mutate(() =>
        {
            var index = SelectedBlock is { } selected ? page.Blocks.IndexOf(selected) + 1 : page.Blocks.Count;
            page.Blocks.Insert(Math.Clamp(index, 0, page.Blocks.Count), block);
            Renumber(page); SelectBlock(block.Id);
        }, "Inserted shared drawing");
        return block;
    }

    public WriteSharedInkSnapshot? CaptureSharedInk(Guid blockId)
    {
        TrackSharedInk();
        var block = Blocks().SingleOrDefault(value => value.Id == blockId);
        if (block is not { Kind: NotesBlockKind.Canvas, Canvas: { } canvas, SharedProductivityBundles: { } bundles }
            || canvas.Strokes.Count != 0 || canvas.Objects.Count != 0 || canvas.GhostLayers.Count != 0)
            return null;
        return new(this, Document, block, Document.Version, _sharedInkEpoch, SelectedBlockId,
            bundles.Select(bundle => bundle.Clone()).ToArray());
    }

    public bool IsSharedInkCurrent(WriteSharedInkSnapshot expected, bool requireSelection = true)
    {
        var current = CaptureSharedInk(expected.Block.Id);
        return current is not null && ReferenceEquals(expected.Owner, this) && ReferenceEquals(expected.Document, Document)
            && ReferenceEquals(current.Block, expected.Block) && expected.Version == Document.Version
            && expected.Epoch == _sharedInkEpoch && expected.Selection == SelectedBlockId
            && (!requireSelection || SelectedBlockId == expected.Block.Id)
            && expected.Bundles.Count == current.Bundles.Count
            && expected.Bundles.Zip(current.Bundles).All(pair => JsonElement.DeepEquals(pair.First, pair.Second));
    }

    /// <summary>The actual Home owner supplies canonical validation; this editor only owns the
    /// native document CAS, full-envelope retention and normal undoable insertion.</summary>
    public bool AppendSharedInk(WriteSharedInkSnapshot expected, JsonElement proposed,
        Func<JsonElement, JsonElement> captureCanonical)
    {
        ArgumentNullException.ThrowIfNull(captureCanonical);
        if (!IsSharedInkCurrent(expected) || expected.Bundles.Count >= 256) return false;
        var captured = captureCanonical(proposed);
        if (captured.ValueKind != JsonValueKind.Object
            || expected.Bundles.Sum(bundle => (long)bundle.GetRawText().Length) + captured.GetRawText().Length > 16 * 1024 * 1024)
            throw new InvalidDataException("The shared drawing exceeds its supported payload bound.");
        var retained = captured.Clone();
        if (!IsSharedInkCurrent(expected)) return false;
        Mutate(() => expected.Block.SharedProductivityBundles!.Add(retained), "Added shared drawing stroke");
        return true;
    }
}
