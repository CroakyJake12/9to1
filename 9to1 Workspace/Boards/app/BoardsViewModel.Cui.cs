using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

// Canonical CUI projection over the existing document/session mutation methods.
// It owns no second document, loader, persistence service, or selection identity.
public sealed partial class BoardsViewModel
{
    private static readonly string[] ContextHorizontalOptions = ["inherit", "left", "center", "right"];
    private static readonly string[] ContextVerticalOptions = ["inherit", "top", "middle", "bottom"];
    private static readonly string[] ContextBorderOptions = ["inherit", "none", "solid", "dashed", "dotted"];
    private static readonly string[] ContextDividerOptions = ["solid", "dashed", "dotted", "none"];
    private static readonly (string Name, string Hex)[] ContextSurfaces =
    [
        ("None", ""), ("White", "#FFFFFFFF"), ("Mist", "#FFF1F3F4"),
        ("Peach", "#FFFFF3C4"), ("Mint", "#FFE6F4EA"), ("Sky", "#FFE8F0FE"),
        ("Lavender", "#FFF3E8FD"), ("Rose", "#FFFCE8E6"), ("Slate", "#FFE8EDF3")
    ];
    private static readonly string[] ContextSurfaceNames = ContextSurfaces.Select(surface => surface.Name).ToArray();

    internal void RefreshCuiContext()
    {
        var block = FindBlock(SelectedBlockId);
        var cellKey = TryGetContextCell(block, out var row, out var column) ? $"{row},{column}" : null;
        var cellStyle = cellKey is not null && block!.CellStyles.TryGetValue(cellKey, out var style) ? style : null;
        Set("ContextHorizontalOptions", ContextHorizontalOptions);
        Set("ContextVerticalOptions", ContextVerticalOptions);
        Set("ContextBorderOptions", ContextBorderOptions);
        Set("ContextDividerOptions", ContextDividerOptions);
        Set("ContextSurfaceNames", ContextSurfaceNames);
        Set("ContextCellHorizontalIndex", IndexOf(ContextHorizontalOptions, cellStyle?.AlignH));
        Set("ContextCellVerticalIndex", IndexOf(ContextVerticalOptions, cellStyle?.AlignV));
        Set("ContextCellSurfaceIndex", Math.Max(0, Array.FindIndex(ContextSurfaces, surface => surface.Hex == cellStyle?.Background)));
        Set("ContextHasCell", cellKey is not null);
        var cellSpan = cellKey is not null && block!.CellSpans.TryGetValue(cellKey, out var projectedSpan)
            ? projectedSpan : null;
        Set("ContextCanMoveRowUp", cellKey is not null && !block!.HasMergedCells && row > 0);
        Set("ContextCanMoveRowDown", cellKey is not null && !block!.HasMergedCells && row < block!.TableRows - 1);
        Set("ContextCanMoveColumnLeft", cellKey is not null && !block!.HasMergedCells && column > 0);
        Set("ContextCanMoveColumnRight", cellKey is not null && !block!.HasMergedCells && column < block!.TableCols - 1);
        Set("ContextCanAddTableRow", block is { Kind: "table", TableRows: < 100 });
        Set("ContextCanDeleteTableRow", block is { Kind: "table", TableRows: > 1 } &&
            !TableEdgeIntersectsMerge(block!, rowEdge: true));
        Set("ContextCanAddTableColumn", block is { Kind: "table", TableCols: < 50 });
        Set("ContextCanDeleteTableColumn", block is { Kind: "table", TableCols: > 1 } &&
            !TableEdgeIntersectsMerge(block!, rowEdge: false));
        Set("ContextCanChangeTableStructure", block is { Kind: "table", HasMergedCells: false });
        Set("ContextCanMergeCellRight", cellSpan?.CanMergeRight == true);
        Set("ContextCanMergeCellDown", cellSpan?.CanMergeDown == true);
        Set("ContextCanSplitCell", cellSpan?.CanSplit == true);
        Set("ContextCellHint", cellKey is null ? "Focus a cell for cell tools." : $"Cell {cellKey}");
        var columnWidth = cellKey is not null && column < block!.ColumnWidths.Count
            ? block.ColumnWidths[column]
            : 0;
        Set("ContextColumnWidthLabel", columnWidth > 0
            ? $"Column {column + 1}: {columnWidth:0} px"
            : cellKey is null ? "Select a cell" : $"Column {column + 1}: Auto");
        var rowHeight = cellKey is not null && row < block!.RowHeights.Count
            ? block.RowHeights[row]
            : 0;
        Set("ContextRowHeightLabel", rowHeight > 0
            ? $"Row {row + 1}: {rowHeight:0} px"
            : cellKey is null ? "Select a cell" : $"Row {row + 1}: Auto");
        Set("ContextColumnWidthDraft", columnWidth);
        Set("ContextRowHeightDraft", rowHeight);
        Set("ContextTableSurfaceIndex", Math.Max(0, Array.FindIndex(ContextSurfaces, surface => surface.Hex == block?.TableStyle.Background)));
        Set("ContextTableBorderIndex", IndexOf(ContextBorderOptions, block?.TableStyle.BorderStyle));
        Set("ContextAlternatingRows", block?.TableStyle.AlternatingRows == true);
        Set("ContextHasHeaderRow", block?.TableStyle.HasHeaderRow == true);
        Set("ContextHasHeaderColumn", block?.TableStyle.HasHeaderColumn == true);
        Set("ContextTableAlignmentIndex", IndexOf(ContextHorizontalOptions, block?.TableStyle.Alignment));
        Set("ContextTableWidthLabel", block is { Kind: "table" } tableBlock && tableBlock.TableStyle.Width > 0
            ? $"Table width: {tableBlock.TableStyle.Width:0} px" : "Table width: Auto");
        Set("ContextImageAlignmentIndex", IndexOf(ContextHorizontalOptions, block?.Image?.Alignment));
        Set("ContextImageKeepAspectRatio", block?.Image?.KeepAspectRatio != false);
        Set("ContextImageFitMode", block?.Image?.FitMode ?? "contain");
        Set("ContextImageCaption", block?.Image?.Caption ?? string.Empty);
        Set("ContextImageAltText", block?.Image?.AltText ?? string.Empty);
        Set("ContextImageLinkUri", block?.Image?.LinkUri ?? string.Empty);
        Set("ContextDividerStyleIndex", IndexOf(ContextDividerOptions, block?.Divider?.LineStyle));
        // Publish branch selection after its data, so a newly created Input has
        // the current selection on its first binding pass.
        Set("ContextIsTable", block?.Kind == "table");
        Set("ContextIsImage", block?.Kind == "image");
        Set("ContextIsDivider", block?.Kind == "divider");
        Set("ContextVisible", _isEditMode && (block?.Kind is "table" or "image" or "divider"));
        RefreshCuiBlockActions();
    }

    private static bool TableEdgeIntersectsMerge(RichBoardBlock block, bool rowEdge)
    {
        if (block.Kind != "table") return true;
        var row = block.TableRows - 1;
        var column = block.TableCols - 1;
        var edgeLength = rowEdge ? block.TableCols : block.TableRows;
        var foundSpanData = false;
        for (var index = 0; index < edgeLength; index++)
        {
            var key = rowEdge ? $"{row},{index}" : $"{index},{column}";
            if (!block.CellSpans.TryGetValue(key, out var span)) continue;
            foundSpanData = true;
            if (span.CoveredByCellId is not null || span.RowSpan > 1 || span.ColumnSpan > 1)
                return true;
        }
        // Older projections may not provide per-cell spans. If the table says
        // it contains merges but the target edge cannot be inspected, refuse
        // the destructive action rather than guess that it is safe.
        return !foundSpanData && block.HasMergedCells;
    }

    private bool _blockActionBusy;

    private bool BlockActionEditingAllowed() => !_textRangeDetached &&
        (_textRangeControl is null || _textRangeBlock != SelectedBlockId ||
            (!_textRangeControl.IsReadOnly && _textRangeControl.IsEffectivelyEnabled));

    private void RefreshCuiBlockActions()
    {
        var blocks = CurrentPage().Blocks;
        var index = blocks.FindIndex(block => block.Id == SelectedBlockId);
        var eligible = index >= 0 && !SelectedBlockId.Contains(':') &&
            blocks[index].Kind is "paragraph" or "heading" or "table" or "image" or "divider" or "graph" or
                "checklist" or "bulleted" or "numbered" or "database" or "tabber";
        var available = _isEditMode && eligible && _session is ContractSessionAdapter && !_blockActionBusy && BlockActionEditingAllowed();
        Set("CanManageSelectedBlock", available);
        Set("CanMoveBlockUp", available && index > 0);
        Set("CanMoveBlockDown", available && index < blocks.Count - 1);
        Set("BlockActionSummary", eligible ? "Selected " + blocks[index].Kind : "Select a flow block");
    }

    private async ValueTask<bool> DispatchCuiBlockActionAsync(string command, CancellationToken cancellationToken)
    {
        BoardBlockAction? action = command switch
        {
            "BlockMoveUp" => BoardBlockAction.MoveUp, "BlockMoveDown" => BoardBlockAction.MoveDown,
            "BlockDuplicate" => BoardBlockAction.Duplicate, "BlockDelete" => BoardBlockAction.Delete, _ => null
        };
        if (action is null) return false;
        if (_session is not ContractSessionAdapter adapter) return true;
        var target = adapter.CaptureBlockAction(SelectedPageId, SelectedBlockId);
        if (target is null) { Set("StatusText", "Select an editable flow block."); return true; }
        return await DispatchCuiBlockActionAsync(new BoardBlockActionRequest(target, action.Value), cancellationToken);
    }

    internal async ValueTask<bool> DispatchCuiBlockActionAsync(
        BoardBlockActionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Action == BoardBlockAction.Delete && !AllowDatabaseStructureChange()) return true;
        if (!_isEditMode || _blockActionBusy || !BlockActionEditingAllowed() ||
            _session is not ContractSessionAdapter adapter ||
            SelectedPageId != request.Target.PageId || SelectedBlockId != request.Target.BlockId)
        { Set("StatusText", "Select an editable flow block."); return true; }
        var epoch = Interlocked.Read(ref _textRangeEpoch);
        _blockActionBusy = true; RefreshCuiBlockActions();
        try
        {
            // Native read-only/enabled/selection changes advance the same range
            // epoch. The gate callback reads no UI properties on a worker thread.
            var result = await adapter.ApplyBlockActionAsync(request.Target, request.Action,
                () => !_textRangeDetached && SelectedPageId == request.Target.PageId && SelectedBlockId == request.Target.BlockId &&
                    Interlocked.Read(ref _textRangeEpoch) == epoch, cancellationToken);
            if (!result.Applied) { Set("StatusText", "Block action refused: " + result.Code); return true; }
            _selectedBlockId = result.SelectedBlockId ?? string.Empty;
            InvalidateTextRange();
            if (FindBlock(_selectedBlockId)?.Kind is "paragraph" or "heading" or "checklist" or "bulleted" or "numbered")
                _pendingTextFocus = (request.Target.PageId, _selectedBlockId, 0);
            RefreshAfterEdit();
            return true;
        }
        finally { _blockActionBusy = false; RefreshCuiBlockActions(); }
    }

    internal async ValueTask<bool> DispatchCuiBlockMoveAsync(
        BoardBlockMoveRequest request, CancellationToken cancellationToken = default)
    {
        if (!_isEditMode || _blockActionBusy || !BlockActionEditingAllowed() ||
            _session is not ContractSessionAdapter adapter ||
            SelectedPageId != request.Target.PageId || SelectedBlockId != request.Target.BlockId)
        { Set("StatusText", "Select an editable flow block."); return false; }
        var epoch = Interlocked.Read(ref _textRangeEpoch);
        _blockActionBusy = true; RefreshCuiBlockActions();
        try
        {
            var result = await adapter.ApplyBlockMoveAsync(request.Target, request.DestinationIndex,
                () => !_textRangeDetached && SelectedPageId == request.Target.PageId && SelectedBlockId == request.Target.BlockId &&
                    Interlocked.Read(ref _textRangeEpoch) == epoch, cancellationToken);
            if (!result.Applied)
            {
                if (result.Code is not "NoChange") Set("StatusText", "Block move refused: " + result.Code);
                return false;
            }
            _selectedBlockId = result.SelectedBlockId ?? request.Target.BlockId;
            InvalidateTextRange();
            RefreshAfterEdit();
            return true;
        }
        finally { _blockActionBusy = false; RefreshCuiBlockActions(); }
    }

    internal void ReportBlockMoveFailure(Exception error) =>
        Set("StatusText", "Block move failed: " + FirstLine(error.Message));

    private static int IndexOf(string[] options, string? value) => Math.Max(0, Array.IndexOf(options, value));

    private bool TryGetContextCell(RichBoardBlock? block, out int row, out int column)
    {
        row = column = -1;
        if (block?.Kind != "table" || SelectedCell is not { } cell || cell.BlockId != block.Id
            || cell.Row < 0 || cell.Row >= block.TableRows || cell.Col < 0 || cell.Col >= block.TableCols) return false;
        row = cell.Row; column = cell.Col; return true;
    }

    public bool TrySetValue(string path, object? value)
    {
        if (_disposed || SessionIsDisposed) return false;
        var key = path[(path.LastIndexOf('.') + 1)..];
        if (key == "IsEditMode" && value is bool editMode)
        {
            SetEditMode(editMode);
            return true;
        }
        if (key == "IsFreeLayout" && value is bool freePlacement)
        {
            if (!CanChangeLayoutMode) return false;
            _ = SetLayoutModeAsync(freePlacement
                ? HavenRichPageLayoutModes.Unlocked
                : HavenRichPageLayoutModes.Locked);
            return true;
        }
        if (key == "LandingSearchQuery")
        {
            SetLandingSearch(value as string);
            return true;
        }
        if (key == "SectionIconPickerSearch" && value is string sectionIconSearch &&
            _sectionIconPickerSectionId is not null && _isEditMode)
        {
            _sectionIconSearchText = sectionIconSearch.Length <= 120 ? sectionIconSearch : sectionIconSearch[..120];
            Set("SectionIconPickerSearch", _sectionIconSearchText);
            RefreshSectionIconPickerChoices();
            return true;
        }
        if (!_isEditMode) return false;
        if (_syncing) return key.StartsWith("Context", StringComparison.Ordinal);
        var block = FindBlock(SelectedBlockId);
        if (block is null) return false;
        if (block.Kind == "table")
        {
            switch (key)
            {
                case "ContextColumnWidthDraft" when TryGetContextCell(block, out _, out _) &&
                    TryReadDimension(value, out var widthDraft) && double.IsFinite(widthDraft) && widthDraft is >= 0 and <= 5000:
                    Set(key, widthDraft); return true;
                case "ContextRowHeightDraft" when TryGetContextCell(block, out _, out _) &&
                    TryReadDimension(value, out var heightDraft) && double.IsFinite(heightDraft) && heightDraft is >= 0 and <= 5000:
                    Set(key, heightDraft); return true;
                case "ContextHasHeaderRow" when value is bool hasHeaderRow:
                    block.TableStyle.HasHeaderRow = hasHeaderRow; CommitViewEdit(); RefreshCuiContext(); return true;
                case "ContextHasHeaderColumn" when value is bool hasHeaderColumn:
                    block.TableStyle.HasHeaderColumn = hasHeaderColumn; CommitViewEdit(); RefreshCuiContext(); return true;
            }
        }
        if (block.Kind == "image" && block.Image is { } image)
        {
            switch (key)
            {
                case "ContextImageCaption" when value is string caption:
                    image.Caption = caption.Length <= 2048 ? caption : caption[..2048]; CommitViewEdit(); RefreshCuiContext(); return true;
                case "ContextImageAltText" when value is string altText:
                    image.AltText = altText.Length <= 2048 ? altText : altText[..2048]; CommitViewEdit(); RefreshCuiContext(); return true;
                case "ContextImageLinkUri" when value is string link:
                    image.LinkUri = link.Length <= 2048 ? link : link[..2048]; CommitViewEdit(); RefreshCuiContext(); return true;
                case "ContextImageKeepAspectRatio" when value is bool keepAspect:
                    image.KeepAspectRatio = keepAspect;
                    if (keepAspect) image.Height = 0;
                    CommitViewEdit(); RefreshCuiContext(); return true;
            }
        }
        bool SelectedOption(string[] options, out string result)
        {
            result = string.Empty;
            if (value is not int index || index < 0 || index >= options.Length) return false;
            result = options[index]; return true;
        }
        switch (key)
        {
            case "ContextCellHorizontalIndex" when TryGetContextCell(block, out var row, out var column):
                if (!SelectedOption(ContextHorizontalOptions, out var horizontal)) return false;
                SetCellAlign(block.Id, row, column, horizontal); RequestRebuild(); break;
            case "ContextCellVerticalIndex" when TryGetContextCell(block, out var verticalRow, out var verticalColumn):
                if (!SelectedOption(ContextVerticalOptions, out var vertical)) return false;
                var cellKey = $"{verticalRow},{verticalColumn}";
                var cellStyle = block.CellStyles.TryGetValue(cellKey, out var existing) ? existing : block.CellStyles[cellKey] = new RichCellStyleView();
                cellStyle.AlignV = vertical; CommitViewEdit(); break;
            case "ContextCellSurfaceIndex" when TryGetContextCell(block, out var fillRow, out var fillColumn):
                if (!SelectedOption(ContextSurfaceNames, out var cellSurface)) return false;
                var selectedSurface = ContextSurfaces.First(surface => surface.Name == cellSurface);
                var fillKey = $"{fillRow},{fillColumn}";
                var fillStyle = block.CellStyles.TryGetValue(fillKey, out var currentFill)
                    ? currentFill
                    : block.CellStyles[fillKey] = new RichCellStyleView();
                fillStyle.Background = selectedSurface.Hex;
                CommitViewEdit(); RequestRebuild(); break;
            case "ContextTableSurfaceIndex" when block.Kind == "table":
                if (value is not int surfaceIndex || surfaceIndex < 0 || surfaceIndex >= ContextSurfaces.Length) return false;
                block.TableStyle.Background = ContextSurfaces[surfaceIndex].Hex; CommitViewEdit(); break;
            case "ContextTableBorderIndex" when block.Kind == "table":
                if (!SelectedOption(ContextBorderOptions, out var border)) return false;
                block.TableStyle.BorderStyle = border; CommitViewEdit(); break;
            case "ContextAlternatingRows" when block.Kind == "table":
                if (value is not bool alternating) return false;
                block.TableStyle.AlternatingRows = alternating; CommitViewEdit(); break;
            case "ContextTableAlignmentIndex" when block.Kind == "table":
                if (!SelectedOption(ContextHorizontalOptions, out var tableAlignment)) return false;
                block.TableStyle.Alignment = tableAlignment; CommitViewEdit(); break;
            case "ContextImageAlignmentIndex" when block.Kind == "image":
                if (!SelectedOption(ContextHorizontalOptions, out var alignment)) return false;
                SetImageAlignment(block.Id, alignment); break;
            case "ContextDividerStyleIndex" when block.Kind == "divider":
                if (!SelectedOption(ContextDividerOptions, out var divider)) return false;
                SetDividerStyle(block.Id, divider); break;
            default: return false;
        }
        RefreshCuiContext(); return true;
    }

    private static bool TryReadDimension(object? value, out double dimension)
    {
        switch (value)
        {
            case double number: dimension = number; return true;
            case decimal number: dimension = (double)number; return true;
            case int number: dimension = number; return true;
            case long number: dimension = number; return true;
            default: dimension = 0; return false;
        }
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;

    private async ValueTask<bool> DispatchCuiContextAsync(string command, CancellationToken cancellationToken)
    {
        if (await DispatchCuiBlockActionAsync(command, cancellationToken)) return true;
        if (!command.StartsWith("Context", StringComparison.Ordinal)) return false;
        if (!_isEditMode) { Set("StatusText", "View mode: switch to Edit to change this board."); return true; }
        cancellationToken.ThrowIfCancellationRequested();
        var block = FindBlock(SelectedBlockId);
        if (block is null) return true;
        switch (command)
        {
            case "ContextAddRow" when block.Kind == "table": TableAddRow(block.Id); break;
            case "ContextDeleteRow" when block.Kind == "table": TableDelRow(block.Id); break;
            case "ContextMoveRowUp" when TryGetContextCell(block, out var moveUpRow, out var moveUpColumn):
                await MoveSelectedTableAxisAsync(block, moveUpRow, moveUpColumn, -1, rowAxis: true, cancellationToken); break;
            case "ContextMoveRowDown" when TryGetContextCell(block, out var moveDownRow, out var moveDownColumn):
                await MoveSelectedTableAxisAsync(block, moveDownRow, moveDownColumn, 1, rowAxis: true, cancellationToken); break;
            case "ContextRowShorter" when TryGetContextCell(block, out var shortRow, out _):
                SetContextRowHeight(block, shortRow, -16); break;
            case "ContextRowTaller" when TryGetContextCell(block, out var tallRow, out _):
                SetContextRowHeight(block, tallRow, 16); break;
            case "ContextRowAutoHeight" when TryGetContextCell(block, out var autoRow, out _):
                SetContextRowHeight(block, autoRow, null); break;
            case "ContextApplyCellDimensions" when TryGetContextCell(block, out var dimensionRow, out var dimensionColumn):
                ApplyContextCellDimensions(block, dimensionRow, dimensionColumn); break;
            case "ContextAddColumn" when block.Kind == "table": TableAddCol(block.Id); break;
            case "ContextDeleteColumn" when block.Kind == "table": TableDelCol(block.Id); break;
            case "ContextMoveColumnLeft" when TryGetContextCell(block, out var moveLeftRow, out var moveLeftColumn):
                await MoveSelectedTableAxisAsync(block, moveLeftRow, moveLeftColumn, -1, rowAxis: false, cancellationToken); break;
            case "ContextMoveColumnRight" when TryGetContextCell(block, out var moveRightRow, out var moveRightColumn):
                await MoveSelectedTableAxisAsync(block, moveRightRow, moveRightColumn, 1, rowAxis: false, cancellationToken); break;
            case "ContextMergeCellRight" when TryGetContextCell(block, out var mergeRightRow, out var mergeRightColumn):
                await ApplySelectedTableCellActionAsync(block, mergeRightRow, mergeRightColumn,
                    BoardTableCellAction.MergeRight, cancellationToken); break;
            case "ContextMergeCellDown" when TryGetContextCell(block, out var mergeDownRow, out var mergeDownColumn):
                await ApplySelectedTableCellActionAsync(block, mergeDownRow, mergeDownColumn,
                    BoardTableCellAction.MergeDown, cancellationToken); break;
            case "ContextSplitCell" when TryGetContextCell(block, out var splitRow, out var splitColumn):
                await ApplySelectedTableCellActionAsync(block, splitRow, splitColumn,
                    BoardTableCellAction.Split, cancellationToken); break;
            case "ContextColumnNarrower" when TryGetContextCell(block, out _, out var narrowColumn):
                SetContextColumnWidth(block, narrowColumn, -24); break;
            case "ContextColumnWider" when TryGetContextCell(block, out _, out var wideColumn):
                SetContextColumnWidth(block, wideColumn, 24); break;
            case "ContextColumnAutoWidth" when TryGetContextCell(block, out _, out var autoColumn):
                SetContextColumnWidth(block, autoColumn, null); break;
            case "ContextTableNarrower" when block.Kind == "table": SetContextTableWidth(block, -40); break;
            case "ContextTableWider" when block.Kind == "table": SetContextTableWidth(block, 40); break;
            case "ContextTableAutoWidth" when block.Kind == "table": SetContextTableWidth(block, null); break;
            case "ContextCellBold" when TryGetContextCell(block, out var row, out var column): ToggleCellBold(block.Id, row, column); break;
            case "ContextCellItalic" when TryGetContextCell(block, out var italicRow, out var italicColumn):
                ToggleCuiCellItalic(block, italicRow, italicColumn); break;
            case "ContextRoundTable" when block.Kind == "table": block.TableStyle.CornerRadius = Math.Min(32, block.TableStyle.CornerRadius + 2); CommitViewEdit(); break;
            case "ContextImageNarrower" when block.Kind == "image" && block.Image is not null: ResizeImage(-40); break;
            case "ContextImageWider" when block.Kind == "image" && block.Image is not null: ResizeImage(40); break;
            case "ContextImageNatural" when block.Kind == "image" && block.Image is not null:
                block.Image.Width = 0; block.Image.Height = 0; block.Image.FitMode = "natural"; CommitViewEdit(); break;
            case "ContextImageShorter" when block.Kind == "image" && block.Image is not null: ResizeImageHeight(-40); break;
            case "ContextImageTaller" when block.Kind == "image" && block.Image is not null: ResizeImageHeight(40); break;
            case "ContextImageRotateLeft" when block.Kind == "image" && block.Image is not null: RotateImage(-90); break;
            case "ContextImageRotateRight" when block.Kind == "image" && block.Image is not null: RotateImage(90); break;
            case "ContextImageCropLeft" when block.Kind == "image" && block.Image is not null: CropImage("left"); break;
            case "ContextImageCropRight" when block.Kind == "image" && block.Image is not null: CropImage("right"); break;
            case "ContextImageCropTop" when block.Kind == "image" && block.Image is not null: CropImage("top"); break;
            case "ContextImageCropBottom" when block.Kind == "image" && block.Image is not null: CropImage("bottom"); break;
            case "ContextImageResetCrop" when block.Kind == "image" && block.Image is not null:
                block.Image.CropLeft = block.Image.CropTop = block.Image.CropRight = block.Image.CropBottom = 0;
                CommitViewEdit(); break;
            case "ContextImageCycleFit" when block.Kind == "image" && block.Image is not null:
                block.Image.FitMode = block.Image.FitMode switch { "contain" => "cover", "cover" => "fill", "fill" => "natural", _ => "contain" };
                CommitViewEdit(); break;
            case "ContextReplaceImage" when block.Kind == "image": await ReplaceImageAsync(block.Id); break;
            case "ContextRemoveImage" when block.Kind == "image": await DeleteBlockAsync(block.Id); break;
            case "ContextDividerThinner" when block.Kind == "divider" && block.Divider is not null: ResizeDivider(-1); break;
            case "ContextDividerThicker" when block.Kind == "divider" && block.Divider is not null: ResizeDivider(1); break;
            default:
                if (block.Kind == "divider" && block.Divider is not null && command.StartsWith("ContextDividerColor", StringComparison.Ordinal))
                {
                    var surface = ContextSurfaces.FirstOrDefault(item => item.Name == command["ContextDividerColor".Length..] && item.Hex.Length > 0);
                    if (surface.Hex is null) return false;
                    block.Divider.Color = surface.Hex; CommitViewEdit();
                }
                else return false;
                break;
        }
        RefreshCuiContext(); return true;
        void ResizeImage(double delta)
        {
            var current = block.Image!.Width > 0 ? block.Image.Width : 320;
            block.Image.Width = Math.Clamp(current + delta, 40, 1600);
            if (block.Image.FitMode == "natural") block.Image.FitMode = "contain";
            CommitViewEdit();
        }
        void ResizeImageHeight(double delta)
        {
            var image = block.Image!;
            image.KeepAspectRatio = false;
            image.Height = Math.Clamp((image.Height > 0 ? image.Height : 240) + delta, 40, 1600);
            CommitViewEdit();
        }
        void RotateImage(double delta)
        {
            var rotation = (block.Image!.RotationDegrees + delta) % 360;
            block.Image.RotationDegrees = rotation;
            CommitViewEdit();
        }
        void CropImage(string edge)
        {
            var image = block.Image!;
            const double step = 0.05;
            switch (edge)
            {
                case "left" when image.CropLeft + step <= 0.9 && image.CropLeft + step + image.CropRight < 1: image.CropLeft += step; break;
                case "right" when image.CropRight + step <= 0.9 && image.CropRight + step + image.CropLeft < 1: image.CropRight += step; break;
                case "top" when image.CropTop + step <= 0.9 && image.CropTop + step + image.CropBottom < 1: image.CropTop += step; break;
                case "bottom" when image.CropBottom + step <= 0.9 && image.CropBottom + step + image.CropTop < 1: image.CropBottom += step; break;
                default: Set("StatusText", "Image crop limit reached. Reset crop to reveal more of the source."); return;
            }
            CommitViewEdit();
        }
        void ResizeDivider(double delta)
        {
            block.Divider!.Thickness = Math.Clamp(block.Divider.Thickness + delta, 1, 12); CommitViewEdit();
        }
    }

    private async Task MoveSelectedTableAxisAsync(RichBoardBlock block, int row, int column,
        int delta, bool rowAxis, CancellationToken cancellationToken)
    {
        if (!RequireEditMode()) return;
        if (_session is not ContractSessionAdapter adapter)
        {
            Set("StatusText", "Table reordering requires the canonical Boards contract session.");
            return;
        }
        var fromIndex = rowAxis ? row : column;
        var toIndex = fromIndex + delta;
        var epoch = Interlocked.Read(ref _tableSelectionEpoch);
        bool IsOriginCurrent() => Interlocked.Read(ref _tableSelectionEpoch) == epoch;
        bool moved;
        try
        {
            moved = await adapter.MoveTableAxisAsync(SelectedPageId, block.Id, rowAxis,
                fromIndex, toIndex, IsOriginCurrent, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            Set("StatusText", "Table reorder failed: " + FirstLine(error.Message));
            return;
        }
        if (!moved)
        {
            Set("StatusText", "The table changed before it could be reordered. Select a cell and try again.");
            return;
        }

        FocusCell(block.Id, rowAxis ? toIndex : row, rowAxis ? column : toIndex);
        RefreshAfterEdit();
        Set("StatusText", rowAxis ? "Table row moved." : "Table column moved.");
    }

    private async Task ApplySelectedTableCellActionAsync(RichBoardBlock block, int row, int column,
        BoardTableCellAction action, CancellationToken cancellationToken)
    {
        if (!RequireEditMode()) return;
        if (_session is not ContractSessionAdapter adapter)
        {
            Set("StatusText", "Table cell merging requires the canonical Boards contract session.");
            return;
        }
        var epoch = Interlocked.Read(ref _tableSelectionEpoch);
        bool IsOriginCurrent() => Interlocked.Read(ref _tableSelectionEpoch) == epoch;
        bool applied;
        try
        {
            applied = await adapter.ApplyTableCellActionAsync(SelectedPageId, block.Id, row, column,
                action, IsOriginCurrent, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            Set("StatusText", "Table cell operation failed: " + FirstLine(error.Message));
            return;
        }
        if (!applied)
        {
            Set("StatusText", "That table cell cannot be merged or split in its current structure.");
            return;
        }
        FocusCell(block.Id, row, column);
        RefreshAfterEdit();
        RefreshCuiContext();
        Set("StatusText", action == BoardTableCellAction.Split
            ? "Table cell split; covered cell values and formatting were restored."
            : "Table cells merged; covered values and formatting remain recoverable on split.");
    }

    private void SetContextColumnWidth(RichBoardBlock block, int column, double? delta)
    {
        if (!RequireEditMode() || block.Kind != "table" || column < 0 || column >= block.TableCols)
            return;
        if (delta is null)
        {
            if (column < block.ColumnWidths.Count) block.ColumnWidths[column] = 0;
        }
        else
        {
            while (block.ColumnWidths.Count <= column) block.ColumnWidths.Add(0);
            var current = block.ColumnWidths[column];
            block.ColumnWidths[column] = current <= 0
                ? (delta.Value < 0 ? 112 : 184)
                : Math.Clamp(current + delta.Value, 80, 1600);
        }
        CommitViewEdit();
        RequestRebuild();
        RefreshCuiContext();
    }

    private void ApplyContextCellDimensions(RichBoardBlock block, int row, int column)
    {
        if (!RequireEditMode() || block.Kind != "table" || row < 0 || row >= block.TableRows ||
            column < 0 || column >= block.TableCols)
            return;
        if (!TryReadDimension(Get("ContextColumnWidthDraft"), out var width) ||
            !TryReadDimension(Get("ContextRowHeightDraft"), out var height) ||
            !double.IsFinite(width) || !double.IsFinite(height) || width is < 0 or > 5000 || height is < 0 or > 5000 ||
            width is > 0 and < 80 || height is > 0 and < 32)
        {
            Set("StatusText", "Use 0 for automatic sizing, or a column width of 80–5000 px and row height of 32–5000 px.");
            return;
        }

        while (block.ColumnWidths.Count <= column) block.ColumnWidths.Add(0);
        while (block.RowHeights.Count <= row) block.RowHeights.Add(0);
        block.ColumnWidths[column] = width;
        block.RowHeights[row] = height;
        CommitViewEdit();
        RequestRebuild();
        RefreshCuiContext();
        Set("StatusText", "Selected table row height and column width applied.");
    }

    private void SetContextTableWidth(RichBoardBlock block, double? delta)
    {
        if (!RequireEditMode() || block.Kind != "table") return;
        block.TableStyle.Width = delta.HasValue
            ? Math.Clamp((block.TableStyle.Width > 0 ? block.TableStyle.Width : 640) + delta.Value, 160, 5000)
            : 0;
        CommitViewEdit();
        RequestRebuild();
        RefreshCuiContext();
    }

    private void ToggleCuiCellItalic(RichBoardBlock block, int row, int column)
    {
        if (!RequireEditMode() || block.Kind != "table" || row < 0 || row >= block.TableRows || column < 0 || column >= block.TableCols)
            return;
        var key = $"{row},{column}";
        var style = block.CellStyles.TryGetValue(key, out var current)
            ? current
            : block.CellStyles[key] = new RichCellStyleView();
        style.Italic = !style.Italic;
        CommitViewEdit();
        RequestRebuild();
        RefreshCuiContext();
    }

    private void SetContextRowHeight(RichBoardBlock block, int row, double? delta)
    {
        if (!RequireEditMode() || block.Kind != "table" || row < 0 || row >= block.TableRows)
            return;
        if (delta is null)
        {
            if (row < block.RowHeights.Count) block.RowHeights[row] = 0;
        }
        else
        {
            while (block.RowHeights.Count <= row) block.RowHeights.Add(0);
            var current = block.RowHeights[row];
            block.RowHeights[row] = current <= 0
                ? (delta.Value < 0 ? 48 : 80)
                : Math.Clamp(current + delta.Value, 32, 1600);
        }
        CommitViewEdit();
        RequestRebuild();
        RefreshCuiContext();
    }
}
