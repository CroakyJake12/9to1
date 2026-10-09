using System.Security.Cryptography;
using System.Text.Json;
using HavenOS.Home.Core;
using Avalonia.Media.Imaging;
using Haven.Application;

namespace HavenOS.Images;

/// <summary>Picture-specific editing over the shared document-suite mutation engine.</summary>
public sealed partial class PictureEditorSession
{
    private readonly DocumentMutationHistory<EditorState> _history;
    private long _revision;
    private int _schemaVersion;
    private string? _savedHash;
    private readonly int _originalWidth, _originalHeight;
    public string? DocumentPath { get; private set; }
    public PictureDocument Document => _history.Current.Document.WithRevision(_revision, _schemaVersion);
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public bool IsDirty { get; private set; }
    public string? LastAction => _history.LastOperation?.Name;
    public DocumentOperationMetadata? LastOperation => _history.LastOperation;
    public long DiscardedEarlierHistoryEntries => _history.DiscardedEarlierHistoryEntries;

    public PictureEditorSession(PictureDocument document, string? documentPath = null)
    {
        var copy = PictureDocument.Deserialize(document.Serialize());
        using var source = PictureCropService.OpenVerifiedSource(copy);
        _originalWidth = source.PixelSize.Width; _originalHeight = source.PixelSize.Height;
        if (copy.SemanticHistory is { } retained)
        {
            var current = copy.WithoutHistory().WithSourceDimensions(_originalWidth, _originalHeight);
            _history = DocumentMutationHistory<EditorState>.RestoreSnapshotHistory(retained,
                PictureHistoryAdapter.OwnerFormat, current.DocumentId, PictureHistoryAdapter.Revision(current.Revision),
                current.SerializeSnapshot(), payload =>
                {
                    var frame = PictureHistoryAdapter.ReadFrame(payload, current);
                    return (new EditorState(frame), frame.DocumentId, PictureHistoryAdapter.Revision(frame.Revision));
                }, CloneState, ProductivitySnapshotHistory.MaximumEntries);
        }
        else
        {
            var initial = copy.RasterHistoryBase(source.PixelSize.Width, source.PixelSize.Height);
            _history = new(new EditorState(initial), CloneState, limit: ProductivitySnapshotHistory.MaximumEntries);
            // Restore the current edit branch through the actual shared mutation engine.
            // Older documents retain only their raster branch, with no fabricated redo/provenance.
            foreach (var operation in copy.Operations)
                _history.Apply("Restore " + operation.GetType().Name.Replace("Operation", ""), DocumentOperationOrigin.Import,
                    state => state.Document = operation switch
                    {
                        CropOperation crop => state.Document.Crop(crop.X, crop.Y, crop.Width, crop.Height),
                        RotateOperation rotate => state.Document.Rotate(rotate.ClockwiseQuarterTurns),
                        FlipOperation flip => state.Document.Flip(flip.Horizontal),
                        // A reordered explicit resize may become a visual no-op.
                        // Keep its saved graph entry and its shared-history step.
                        ResizeOperation resize when resize.Width == state.Document.CanvasWidth && resize.Height == state.Document.CanvasHeight =>
                            PictureOperationEditor.Replay(state.Document, [.. state.Document.Operations, resize], _originalWidth, _originalHeight),
                        ResizeOperation resize => state.Document.Resize(resize.Width, resize.Height),
                        // Preserve an explicit saved canvas step even when an
                        // earlier stack edit makes its geometry a visual no-op.
                        CanvasResizeOperation canvasResize => PictureOperationEditor.Replay(state.Document,
                            [.. state.Document.Operations, canvasResize], _originalWidth, _originalHeight),
                        StraightenOperation angle => PictureOperationEditor.Replay(state.Document,
                            [.. state.Document.Operations, angle], _originalWidth, _originalHeight),
                        ColorAdjustmentOperation color => PictureOperationEditor.Replay(state.Document,
                            [.. state.Document.Operations, color], _originalWidth, _originalHeight),
                        BlurOperation blur => PictureOperationEditor.Replay(state.Document,
                            [.. state.Document.Operations, blur], _originalWidth, _originalHeight),
                        PixelationOperation pixels => PictureOperationEditor.Replay(state.Document,
                            [.. state.Document.Operations, pixels], _originalWidth, _originalHeight),
                        _ => throw new InvalidDataException("Unsupported Picture operation.")
                    });
        }
        _revision = copy.Revision;
        _schemaVersion = _history.Current.Document.SchemaVersion;
        DocumentPath = documentPath is null ? null : Path.GetFullPath(documentPath);
        _savedHash = DocumentPath is null ? null : HashFile(DocumentPath);
        IsDirty = DocumentPath is null && copy.SourcePath is null;
    }

    public Bitmap Apply(string name, Func<PictureDocument, PictureDocument> edit)
    {
        var current = Document;
        var updated = edit(current);
        if (ReferenceEquals(updated, current)) return PictureCropService.Render(current);
        var nextRevision = checked(_revision + 1);
        updated = updated.WithRevision(nextRevision);
        var rendered = PictureCropService.Render(updated);
        try { _history.Apply(name, DocumentOperationOrigin.User, state => state.Document = updated); }
        catch { rendered.Dispose(); throw; }
        _revision = nextRevision;
        _schemaVersion = Math.Max(_schemaVersion, updated.SchemaVersion);
        IsDirty = true;
        return rendered;
    }

    public PictureOperationTarget CaptureOperation(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var document = Document;
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, document.Operations.Count);
        return new(this, document.DocumentId, document.Revision, index, document.Operations[index]);
    }

    public Bitmap RemoveOperation(PictureOperationTarget target)
    {
        DemandCurrentOperation(target);
        return Apply("Remove edit", document =>
            PictureOperationEditor.RemoveAt(document, target.Index, _originalWidth, _originalHeight));
    }

    public Bitmap MoveOperation(PictureOperationTarget target, int destinationIndex)
    {
        DemandCurrentOperation(target);
        return Apply("Reorder edit", document => destinationIndex == target.Index ? document :
            PictureOperationEditor.Move(document, target.Index, destinationIndex, _originalWidth, _originalHeight));
    }

    public Bitmap ReplaceOperation(PictureOperationTarget target, PictureOperation replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        DemandCurrentOperation(target);
        return Apply("Modify edit", document => Equals(target.Operation, replacement) ? document :
            PictureOperationEditor.ReplaceAt(document, target.Index, replacement, _originalWidth, _originalHeight));
    }

    private void DemandCurrentOperation(PictureOperationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var current = Document;
        if (!ReferenceEquals(target.Owner, this) || target.DocumentId != current.DocumentId ||
            target.Revision != current.Revision || target.Index < 0 || target.Index >= current.Operations.Count ||
            !ReferenceEquals(target.Operation, current.Operations[target.Index]))
            throw new InvalidOperationException("Revision conflict: select this edit again from the current document.");
    }

    public Bitmap AddSharedVector(HomeProductivityObject originalVector, PictureCompositionTarget? destinationLayer = null)
    {
        if (destinationLayer is not null) DemandComposition(destinationLayer, PictureCompositionTargetKind.Layer);
        return ApplyComposition("Insert shared vector", document => PictureCompositionAdapter.AddVector(document, originalVector, destinationLayer?.TargetId));
    }

    public Bitmap CreateLayer(string name) => ApplyComposition("Create layer", document =>
    {
        var graph = PictureCompositionAdapter.Read(document);
        var initialized = document.CompositionState is null ? document.WithComposition(CanvasArtifactCodec.Serialize(graph)) : document;
        return PictureCompositionAdapter.Mutate(initialized, graph.RevisionId,
            (owner, page) => owner.CreateLayer(PictureCompositionAdapter.Request(owner), page, name));
    });

    public PictureCompositionTarget CaptureComposition(Guid targetId, PictureCompositionTargetKind kind)
    {
        var document = Document;
        if (document.CompositionState is null) throw new InvalidOperationException("This document has no retained composition selection.");
        var graph = PictureCompositionAdapter.Read(document);
        var page = graph.Pages[0];
        if (kind == PictureCompositionTargetKind.Layer ? !page.Layers.Any(layer => layer.LayerId == targetId) :
            !page.Objects.Any(item => item.ObjectId == targetId && item.ObjectTypeId == "drawing.vector"))
            throw new InvalidOperationException("The selected canonical layer or vector is unavailable.");
        return new(this, document.DocumentId, document.Revision, graph.RevisionId, targetId, kind);
    }

    public Bitmap RenameLayer(PictureCompositionTarget target, string name) => MutateLayer(target, "Rename layer",
        (owner, page) => owner.RenameLayer(PictureCompositionAdapter.Request(owner), page, target.TargetId, name));
    public Bitmap SetLayerVisibility(PictureCompositionTarget target, bool visible) => MutateLayer(target, "Change layer visibility",
        (owner, page) => owner.SetLayerVisibility(PictureCompositionAdapter.Request(owner), page, target.TargetId, visible));
    public Bitmap SetLayerLocked(PictureCompositionTarget target, bool locked) => MutateLayer(target, "Change layer lock",
        (owner, page) => owner.SetLayerLocked(PictureCompositionAdapter.Request(owner), page, target.TargetId, locked));
    public Bitmap MoveLayer(PictureCompositionTarget target, int destination) => MutateLayer(target, "Reorder layer",
        (owner, page) => owner.MoveLayer(PictureCompositionAdapter.Request(owner), page, target.TargetId, destination));

    public Bitmap MoveVector(PictureCompositionTarget target, CanvasRect placement)
    {
        DemandComposition(target, PictureCompositionTargetKind.Vector);
        return ApplyComposition("Move shared vector", document =>
        {
            var original = PictureCompositionAdapter.Read(document).Pages[0].Objects.Single(item => item.ObjectId == target.TargetId);
            return Equals(original.Geometry, placement) ? document.CompositionState : PictureCompositionAdapter.Mutate(document, target.GraphRevision,
                (owner, page) => owner.MoveObject(PictureCompositionAdapter.Request(owner), page, target.TargetId, placement));
        });
    }

    public Bitmap SetVectorFill(PictureCompositionTarget target, Guid pathId, string color)
    {
        DemandComposition(target, PictureCompositionTargetKind.Vector);
        return ApplyComposition("Change shared vector fill", document => PictureCompositionAdapter.Mutate(document, target.GraphRevision,
            (owner, pageId) =>
            {
                var item = owner.GetArtifactSnapshot().Pages.Single(page => page.PageId == pageId).Objects.Single(item => item.ObjectId == target.TargetId);
                var original = PictureCompositionAdapter.ReadVector(item);
                // Existing canonical Home vector semantics preserve every retained
                // unknown JSON property. The same graph owner commits the result.
                var action = new HomeProductivityAction("vector.fill", 1, "drawing.vector", [original.ObjectId],
                    JsonSerializer.SerializeToElement(new { pathId = pathId.ToString("D"), color }), document.Revision)
                { ExpectedArtifactRevision = new(VersionId: target.GraphRevision) };
                var updated = new HomeVectorShapeObjectHandler().Transform(original, action);
                _ = new HomeVectorShapeObjectHandler().Render(updated);
                return owner.UpdateSharedObject(PictureCompositionAdapter.Request(owner), pageId,
                    item with { SharedPayload = JsonSerializer.SerializeToElement(updated) });
            }));
    }

    private Bitmap MutateLayer(PictureCompositionTarget target, string name,
        Func<CanvasArtifactSession, Guid, CanvasApiResult<CanvasMutationResult>> change)
    {
        DemandComposition(target, PictureCompositionTargetKind.Layer);
        return ApplyComposition(name, document => PictureCompositionAdapter.Mutate(document, target.GraphRevision, change));
    }

    private Bitmap ApplyComposition(string name, Func<PictureDocument, byte[]?> change) => Apply(name, document =>
    {
        var bytes = change(document);
        return ReferenceEquals(bytes, document.CompositionState) || (bytes is not null && document.CompositionState is not null &&
            bytes.AsSpan().SequenceEqual(document.CompositionState)) ? document : document.WithComposition(bytes
                ?? throw new InvalidDataException("A changed composition must retain its canonical graph."));
    });

    private void DemandComposition(PictureCompositionTarget target, PictureCompositionTargetKind kind)
    {
        ArgumentNullException.ThrowIfNull(target);
        var current = Document;
        if (!ReferenceEquals(target.Owner, this) || target.Kind != kind || target.DocumentId != current.DocumentId ||
            target.DocumentRevision != current.Revision || current.CompositionState is null ||
            target.GraphRevision != PictureCompositionAdapter.Read(current).RevisionId)
            throw new InvalidOperationException("Revision conflict: select the current canonical composition again.");
        _ = CaptureComposition(target.TargetId, kind);
    }

    public Bitmap Undo() => MoveHistory(redo: false);
    public Bitmap Redo() => MoveHistory(redo: true);

    private Bitmap MoveHistory(bool redo)
    {
        if (redo ? !CanRedo : !CanUndo) throw new InvalidOperationException("No edit is available in that history direction.");
        var nextRevision = checked(_revision + 1);
        using var source = PictureCropService.OpenVerifiedSource(Document);
        if (redo) _history.Redo(); else _history.Undo();
        try
        {
            var rendered = PictureCropService.Render(source, _history.Current.Document);
            _revision = nextRevision;
            IsDirty = true;
            return rendered;
        }
        catch
        {
            if (redo) _history.Undo(); else _history.Redo();
            throw;
        }
    }

    public async Task SaveAsync(string path, bool saveCopy = false, CancellationToken cancellationToken = default)
    {
        var destination = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Document.SourcePath is { } sourcePath && string.Equals(destination, Path.GetFullPath(sourcePath), comparison))
            throw new IOException("The editable document cannot replace its source image.");
        var sameDocument = !saveCopy && DocumentPath is not null && string.Equals(destination, DocumentPath, comparison);
        if (File.Exists(destination))
        {
            if (!sameDocument) throw new IOException("That destination already exists. Choose a new name to preserve it.");
            if (_savedHash != HashFile(destination))
                throw new IOException("Revision conflict: the saved document changed in another editor. Save a Copy to preserve both versions.");
        }
        else if (sameDocument)
            throw new IOException("The saved document moved or disappeared. Save a Copy to choose its new location.");
        // A copy is a new history owner with its own revision0. The original
        // session/asset retains every original frame; do not attach old-owner
        // snapshots or fabricate earlier revisions under the copy's identity.
        var document = saveCopy ? Document.CreateCopy() : CaptureDocumentForSave();
        await document.SaveAsync(destination, cancellationToken);
        if (!saveCopy)
        {
            DocumentPath = destination;
            _savedHash = HashFile(destination);
            IsDirty = false;
        }
    }

    /// <summary>Editable document plus SAME canonical retained history for the
    /// owning Files save adapter. This is data, not a Files/Home write grant.</summary>
    public PictureDocument CaptureDocumentForSave()
    {
        var current = Document.WithSourceDimensions(_originalWidth, _originalHeight);
        var history = _history.CaptureSnapshotHistory(PictureHistoryAdapter.OwnerFormat, current.DocumentId,
            PictureHistoryAdapter.Revision(current.Revision), current.SerializeSnapshot(), state =>
            {
                var frame = state.Document.WithSourceDimensions(_originalWidth, _originalHeight);
                return (frame.SerializeSnapshot(), PictureHistoryAdapter.Revision(frame.Revision));
            });
        return current.WithHistory(history);
    }

    private static EditorState CloneState(EditorState state) => new(PictureDocument.DeserializeSnapshot(state.Document.SerializeSnapshot()));

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class EditorState(PictureDocument document)
    {
        public PictureDocument Document { get; set; } = document;
    }
}
