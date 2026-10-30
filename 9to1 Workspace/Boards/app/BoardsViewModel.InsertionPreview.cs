namespace CakeOS.Apps.Boards.App;

internal enum BlockInsertionPlacement { Above, Below, Replace }
internal enum BlockPreviewInput { Pointer, Focus }

// Transient UI ownership, deliberately separate from the persisted board and undo history.
internal sealed record BlockInsertionPreviewOwner(IRichBoardSession Session, RichBoardDocument Document,
    string PageId, string AnchorBlockId, RichBoardBlock Row, long Generation);
internal sealed record BlockInsertionPreview(BlockInsertionPreviewOwner Owner, BlockInsertionPlacement Placement);
internal sealed record BlockPreviewRequest(BlockInsertionPreviewOwner Owner, object Stage,
    BlockInsertionPlacement Placement, BlockPreviewInput Input, bool Enter);

public sealed partial class BoardsViewModel
{
    private BlockInsertionPreviewOwner? _insertionPreviewOwner;
    private object? _insertionPreviewStage;
    private (BlockPreviewRequest Request, long Order)? _pointerPreview, _focusPreview;
    private BlockInsertionPlacement? _pinnedInsertionPlacement;
    private long _insertionPreviewGeneration, _insertionPreviewOrder;
    internal BlockInsertionPreview? InsertionPreview { get; private set; }
    internal event Action? InsertionPreviewChanged;

    internal BlockInsertionPreviewOwner? BeginInsertionPreview(string anchorBlockId, string rowId)
    {
        CancelInsertionPreview();
        if (_disposed || EditorLifetimeEnded || !_isEditMode || !CanEdit || !ShowNativeAuthoring) return null;
        var row = PageBlocks.FirstOrDefault(block => block.Id == rowId);
        if (row is null) return null;
        _insertionPreviewOwner = new(_session, _session.Document, SelectedPageId, anchorBlockId, row,
            ++_insertionPreviewGeneration);
        InsertionPreviewChanged?.Invoke();
        return _insertionPreviewOwner;
    }

    internal bool IsInsertionPreviewCurrent(BlockInsertionPreviewOwner owner) =>
        ReferenceEquals(owner, _insertionPreviewOwner) && !_disposed && !EditorLifetimeEnded &&
        _isEditMode && CanEdit && ShowNativeAuthoring && ReferenceEquals(owner.Session, _session) &&
        ReferenceEquals(owner.Document, _session.Document) && owner.PageId == SelectedPageId &&
        PageBlocks.Any(block => ReferenceEquals(block, owner.Row));

    internal object BeginInsertionPreviewStage(BlockInsertionPreviewOwner owner)
    {
        var stage = new object();
        if (!IsInsertionPreviewCurrent(owner)) return stage;
        _insertionPreviewStage = stage;
        _pinnedInsertionPlacement = null;
        _pointerPreview = _focusPreview = null;
        PublishInsertionPreview();
        return stage;
    }

    internal void UpdateInsertionPreview(BlockPreviewRequest request)
    {
        ValidateInsertionPreview();
        if (!IsInsertionPreviewCurrent(request.Owner) ||
            !ReferenceEquals(request.Stage, _insertionPreviewStage) || _pinnedInsertionPlacement is not null) return;
        ref var slot = ref (request.Input == BlockPreviewInput.Pointer ? ref _pointerPreview : ref _focusPreview);
        if (request.Enter) slot = (request, ++_insertionPreviewOrder);
        else if (slot is { } current && current.Request.Placement == request.Placement &&
            ReferenceEquals(current.Request.Stage, request.Stage)) slot = null;
        PublishInsertionPreview();
    }

    internal bool PinInsertionPreview(BlockInsertionPreviewOwner owner, object stage, BlockInsertionPlacement placement)
    {
        if (!IsInsertionPreviewCurrent(owner) || !ReferenceEquals(stage, _insertionPreviewStage)) return false;
        _pinnedInsertionPlacement = placement;
        _pointerPreview = _focusPreview = null;
        PublishInsertionPreview();
        return true;
    }

    internal void EndInsertionPreview(BlockInsertionPreviewOwner owner)
    {
        if (ReferenceEquals(owner, _insertionPreviewOwner)) CancelInsertionPreview();
    }

    private void ValidateInsertionPreview()
    {
        if (_insertionPreviewOwner is { } owner && !IsInsertionPreviewCurrent(owner)) CancelInsertionPreview();
    }

    private void CancelInsertionPreview()
    {
        if (_insertionPreviewOwner is null && InsertionPreview is null) return;
        _insertionPreviewOwner = null;
        _insertionPreviewStage = null;
        _pointerPreview = _focusPreview = null;
        _pinnedInsertionPlacement = null;
        InsertionPreview = null;
        InsertionPreviewChanged?.Invoke();
    }

    private void PublishInsertionPreview()
    {
        var latest = _pointerPreview is { } pointer &&
            (_focusPreview is not { } focus || pointer.Order > focus.Order) ? _pointerPreview : _focusPreview;
        var placement = _pinnedInsertionPlacement ?? latest?.Request.Placement;
        var next = placement is { } value && _insertionPreviewOwner is { } owner
            ? new BlockInsertionPreview(owner, value) : null;
        if (InsertionPreview == next) return;
        InsertionPreview = next;
        InsertionPreviewChanged?.Invoke();
    }
}
