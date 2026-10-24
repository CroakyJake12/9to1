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
    private readonly Dictionary<Guid, Guid> _uncommittedPageInkIds = [];
    private Guid? _cachedPageInkPage;
    private string? _cachedPageInkRaw;
    private NotesSharedInkLayer? _cachedPageInk;
    private long _pageInkMetadataEpoch;
    private NotesSharedInkLayer? ReadPageInk(Guid pageId, string? raw)
    {
        if (_cachedPageInkPage == pageId && string.Equals(_cachedPageInkRaw, raw, StringComparison.Ordinal))
        { _cachedPageInkRaw = raw; return _cachedPageInk; }
        // Observing an incompatible value also retires the previous capture.
        // Keep the last valid cache until parsing succeeds; never cache bad data as blank.
        checked { ++_pageInkMetadataEpoch; }
        var parsed = NotesSharedInkLayer.Read(Document, pageId);
        _cachedPageInkPage = pageId; _cachedPageInkRaw = raw; _cachedPageInk = parsed;
        return parsed; // One page-scoped immutable parse cache, never canonical state or a new store.
    }

    /// <summary>True only for the explicit page-origin container; placement locking
    /// protects its anchor, independently of Edit/View and ink authoring.</summary>
    public static bool IsPageInkLayer(NotesDocument document, NotesPage page, NotesCanvasObject item)
    {
        if (item.Kind != NotesCanvasObjectKind.Ink) return false;
        try
        {
            var layer = NotesSharedInkLayer.Read(document, page.Id);
            return item.Kind == NotesCanvasObjectKind.Ink && layer is not null && layer.LayerId == item.Id && item.Id != Guid.Empty && item.Locked &&
                item.X == 0 && item.Y == 0 && item.Rotation == 0 && item.VectorShape is null &&
                double.IsFinite(item.Width) && double.IsFinite(item.Height) && item.Width > 0 && item.Height > 0;
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or NotSupportedException) { return false; }
    }

    public WritePageSharedInkSnapshot? CapturePageSharedInk(Guid pageId)
    {
        TrackSharedInk();
        var page = Document.Sections.SelectMany(section => section.Pages).SingleOrDefault(value => value.Id == pageId);
        if (page is null || PageScopeId is { } scope && scope != pageId ||
            !double.IsFinite(page.CanvasWidth) || !double.IsFinite(page.CanvasHeight) ||
            page.CanvasWidth is < 100 or > 100_000 || page.CanvasHeight is < 100 or > 100_000)
        { checked { ++_pageInkMetadataEpoch; } return null; }
        string? metadataKey, metadata;
        try
        {
            metadataKey = NotesSharedInkLayer.ReadMetadataKey(Document, pageId);
            metadata = metadataKey is null ? null : NotesSharedInkLayer.ReadMetadata(Document, pageId);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or NotSupportedException)
        { checked { ++_pageInkMetadataEpoch; } throw; }
        var envelope = ReadPageInk(pageId, metadata);
        NotesCanvasObject? layer = null;
        if (envelope is not null)
        {
            var layers = page.CanvasObjects.Where(item => item.Id == envelope.LayerId).Take(2).ToArray();
            if (layers.Length != 1 || layers[0].Kind != NotesCanvasObjectKind.Ink || !layers[0].Locked ||
                layers[0].X != 0 || layers[0].Y != 0 || layers[0].Rotation != 0 || layers[0].VectorShape is not null ||
                !double.IsFinite(layers[0].Width) || !double.IsFinite(layers[0].Height) || layers[0].Width <= 0 || layers[0].Height <= 0)
            {
                checked { ++_pageInkMetadataEpoch; }
                throw new InvalidDataException("The retained page ink extension has an absent, ambiguous or incompatible original Ink object. Its data is preserved; repair/rebind explicitly.");
            }
            layer = layers[0];
        }
        if (layer is null && !_uncommittedPageInkIds.ContainsKey(pageId)) _uncommittedPageInkIds.Add(pageId, Guid.NewGuid());
        return new(this, Document, page, layer, layer?.Id ?? _uncommittedPageInkIds[pageId], Document.Version,
            _sharedInkEpoch, _pageInkMetadataEpoch, SelectedBlockId, page.CanvasWidth, page.CanvasHeight, envelope, metadataKey, metadata, envelope?.Bundles ?? []);
    }

    public bool IsPageSharedInkCurrent(WritePageSharedInkSnapshot expected)
    {
        var current = CapturePageSharedInk(expected.Page.Id);
        return current is not null && ReferenceEquals(expected.Owner, this) && ReferenceEquals(expected.Document, Document) &&
            ReferenceEquals(current.Page, expected.Page) && ReferenceEquals(current.Layer, expected.Layer) && current.LayerId == expected.LayerId &&
            expected.Version == Document.Version && expected.Epoch == _sharedInkEpoch && expected.MetadataEpoch == current.MetadataEpoch && expected.Selection == SelectedBlockId &&
            expected.Width == current.Width && expected.Height == current.Height && expected.MetadataKey == current.MetadataKey && expected.MetadataSource == current.MetadataSource &&
            expected.Bundles.Count == current.Bundles.Count && expected.Bundles.Zip(current.Bundles).All(pair => JsonElement.DeepEquals(pair.First, pair.Second));
    }

    /// <summary>One undo boundary for append or exact erasure. Validation belongs to
    /// Home; the original Notes document remains the sole persistence/CAS owner.</summary>
    public bool ReplacePageSharedInk(WritePageSharedInkSnapshot expected, IReadOnlyList<JsonElement> proposed,
        Func<IReadOnlyList<JsonElement>, IReadOnlyList<JsonElement>> captureCanonical, double? width = null, double? height = null)
    {
        ArgumentNullException.ThrowIfNull(captureCanonical);
        if (!IsPageSharedInkCurrent(expected)) return false;
        var extentWidth = Math.Max(expected.Width, width ?? expected.Width);
        var extentHeight = Math.Max(expected.Height, height ?? expected.Height);
        if (!double.IsFinite(extentWidth) || !double.IsFinite(extentHeight) || extentWidth > 100_000 || extentHeight > 100_000)
            throw new InvalidDataException("The page drawing exceeds the supported document-space extent.");
        var canonical = captureCanonical(proposed);
        var envelope = expected.Envelope?.WithBundles(canonical)
            ?? NotesSharedInkLayer.Create(expected.Page.Id, expected.LayerId, canonical);
        var raw = envelope.ToJson().GetRawText();
        var metadataKey = expected.MetadataKey ?? NotesSharedInkLayer.MetadataKey(expected.Page.Id);
        // Use the same old schema1 serializer, measuring actual escaped UTF8 bytes,
        // before modifying the original document or installing an undo preimage.
        var preview = Clone(Document);
        var previewPage = preview.Sections.SelectMany(section => section.Pages).Single(page => page.Id == expected.Page.Id);
        if (expected.Layer is null) previewPage.CanvasObjects.Add(new NotesCanvasObject { Id = expected.LayerId, Kind = NotesCanvasObjectKind.Ink,
            Locked = true, Width = extentWidth, Height = extentHeight });
        var previewLayer = previewPage.CanvasObjects.Single(item => item.Id == expected.LayerId);
        previewLayer.Width = extentWidth; previewLayer.Height = extentHeight;
        previewPage.CanvasWidth = extentWidth; previewPage.CanvasHeight = extentHeight;
        preview.Metadata[metadataKey] = raw;
        using (var measured = new PageInkSizeMeasurement()) JsonSerializer.Serialize(measured, preview, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
        if (!IsPageSharedInkCurrent(expected)) return false;
        Mutate(() =>
        {
            var layer = expected.Layer;
            if (layer is null)
            {
                layer = new NotesCanvasObject { Id = expected.LayerId, Kind = NotesCanvasObjectKind.Ink,
                    X = 0, Y = 0, Rotation = 0, Locked = true, Width = expected.Page.CanvasWidth,
                    Height = expected.Page.CanvasHeight };
                expected.Page.CanvasObjects.Add(layer);
            }
            // Only a real authored drawing operation can grow the document extent.
            // Scrolling, layout and DPI changes never rewrite stored coordinates.
            expected.Page.CanvasWidth = extentWidth; expected.Page.CanvasHeight = extentHeight;
            layer.Width = extentWidth; layer.Height = extentHeight;
            Document.Metadata[metadataKey] = raw;
        }, "Changed page drawing");
        return true;
    }

    private sealed class PageInkSizeMeasurement : Stream
    {
        private long _bytes;
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => _bytes; public override long Position { get => _bytes; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Count(count);
        public override void Write(ReadOnlySpan<byte> buffer) => Count(buffer.Length);
        private void Count(int count)
        {
            _bytes = checked(_bytes + count);
            if (_bytes > 63L * 1024 * 1024) throw new InvalidDataException("The drawing would exceed the 63MiB authored body budget (1MiB receipt/history headroom within the 64MiB native body bound); original data is unchanged.");
        }
    }

}

public sealed record WritePageSharedInkSnapshot(WriteDocumentEditor Owner, NotesDocument Document, NotesPage Page,
    NotesCanvasObject? Layer, Guid LayerId, long Version, long Epoch, long MetadataEpoch, Guid? Selection, double Width, double Height, NotesSharedInkLayer? Envelope, string? MetadataKey, string? MetadataSource, IReadOnlyList<JsonElement> Bundles);
