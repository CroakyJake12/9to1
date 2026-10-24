using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace NineToOne.Web.Productivity.Boards;

/// <summary>Presentation and typed page actions over the existing Boards owner, with no private document/store.</summary>
public sealed partial class BoardsBrowserCore
{
    private bool _showPicker, _showTrash, _showDetails;
    private bool _compact, _drawerOpen, _pageInkCanvasActive;
    internal void ConfigurePageInkCanvas(bool active)
    { if (_pageInkCanvasActive == active) return; _pageInkCanvasActive = active; Notify(); }
    private string _boardSearch = string.Empty;
    public Guid? ActivePageId { get; private set; }
    private IEnumerable<NotesPage> VisiblePages => _document?.Sections.SelectMany(section =>
        section.Pages.OrderBy(page => page.Order)).Where(page => !BoardsWorkspaceService.IsPageDeleted(_document!, page.Id)) ?? [];
    private NotesPage? ActivePage => VisiblePages.FirstOrDefault(page => page.Id == ActivePageId);
    private NotesSection? ActiveSection => _document?.Sections.FirstOrDefault(section => section.Pages.Any(page => page.Id == ActivePageId));
    private bool CanChangeBoard => _document is not null && !_disposed && !_presentationPending && !IsBusy && !IsReadOnly && !IsHistoryPreview && _pending is null;

    internal void NotifySelectionChanged() { checked { ++_sharedInkSelectionEpoch; } Notify(); }
    internal void SetViewportWidth(double width)
    {
        var compact = width < 760;
        if (_compact == compact) return;
        _compact = compact; _drawerOpen = false; Notify();
    }

    private void RememberBoard()
    {
        if (_document is not { } document) return;
        var summary = new NotesDocumentSummary(document.Id, document.Title, document.UpdatedAt, DurableRevision,
            document.Sections.Count, document.Sections.Sum(section => section.Pages.Sum(page => page.Blocks.Count)),
            NotesTextStatistics.Calculate(document).Words, document.Recovery.HasUnsavedRecovery);
        _notebooks = _notebooks.Where(row => row.Id != document.Id).Append(summary).OrderByDescending(row => row.UpdatedAt).ToArray();
    }

    private bool TryGetWorkspaceValue(string path, out object? value)
    {
        if (TryGetTableValue(path, out value)) return true;
        if (TryGetNoteBlockValue(path, out value)) return true;
        var mountedPageLayer = (path is "PageInkCanvas" or "HasUnsupportedPageContent") && SharedInkAvailable
            ? CapturePageSharedDrawing()?.Content.Layer : null;
        value = path switch
        {
            "HasBoard" => _document is not null,
            "WorkspaceColumns" => _compact ? "1fr" : "240 1fr",
            "EditorColumn" => _compact ? 0 : 1,
            "IsCompact" => _compact,
            "ShowSidebar" => !_compact || _drawerOpen,
            "DrawerWidth" => _compact ? 280d : 240d,
            "HasNoBoard" => _document is null,
            "HasBoards" => _notebooks.Count > 0,
            "NoBoards" => _notebooks.Count == 0,
            "BoardTitle" => _document?.Title ?? "Your boards",
            "PageTitle" => ActivePage?.Title ?? string.Empty,
            "Breadcrumb" => ActiveSection?.Title ?? "Boards",
            "BoardSearch" => _boardSearch,
            "ShowBoardPicker" => _showPicker || _document is null,
            "ShowPageTree" => _document is not null && !_showPicker,
            "ShowTrash" => _showTrash,
            "ShowDetails" => _showDetails,
            "CanChangeBoard" => CanChangeBoard,
            "CanEditPage" => AllowRetainedRichInput,
            "IsEditMode" => _document is not null && ActivePageId is { } editId && _boards.GetEditMode(_document, editId) == BoardsPageEditMode.Edit,
            "LayoutUnlocked" => _document is not null && ActivePageId is { } layoutId && _boards.GetLayoutMode(_document, layoutId) == BoardsPageLayoutMode.Unlocked,
            "EditModeLabel" => IsReadOnly ? "Read-only access" : RichEditingPermitted ? "Editing" : "Viewing · content protected",
            "PageRows" => _document?.Sections.SelectMany(section => section.Pages.OrderBy(page => page.Order)
                .Where(page => !BoardsWorkspaceService.IsPageDeleted(_document!, page.Id))
                .Select(page => new BoardsPageRow(page.Id, page.Title, section.Title, page.Id == ActivePageId))).ToArray() ?? [],
            "DeletedPages" => _document is null ? Array.Empty<BoardsPageRow>() : _boards.ListDeletedPages(_document)
                .Select(page => new BoardsPageRow(page.Id, page.Title, "Trash", false)).ToArray(),
            "HasDeletedPages" => _document is not null && _boards.ListDeletedPages(_document).Count > 0,
            "PagePosition" => ActivePage is null ? string.Empty : $"{VisiblePages.Count()} pages · {ActivePage.Blocks.Count} blocks",
            "SelectionLabel" => _richEditor?.SelectedBlock is { } block ? block.Kind.ToString() : "Page",
            "IsChecklistSelected" => _richEditor?.SelectedBlock?.List is { Kind: NotesListKind.Checklist },
            "PageInkCanvas" => SharedInkAvailable && (_pageInkCanvasActive || mountedPageLayer is not null),
            "SharedInkAvailable" => SharedInkAvailable,
            "SharedInkUnavailable" => !SharedInkAvailable,
            "HasSharedDrawings" => ActivePage?.Blocks.Any(block => block.SharedProductivityBundles is not null) == true,
            "HasSelectedSharedDrawing" => CaptureSharedDrawing() is not null,
            "SharedDrawingRows" => ActivePage?.Blocks.Where(block => block.SharedProductivityBundles is not null)
                .Select((block, index) => new BoardsDrawingRow(block.Id, $"Drawing {index + 1}")).ToArray() ?? [],
            "HasUnsupportedPageContent" => ActivePage is { } current && (current.CanvasObjects.Any(item => !SharedInkAvailable || !ReferenceEquals(item, mountedPageLayer)) || current.Blocks.Any(block =>
                block.Kind == NotesBlockKind.HtmlWidget || block.Kind == NotesBlockKind.Canvas && block.SharedProductivityBundles is null)),
            "ChecklistItems" => _richEditor?.SelectedBlock is { List: { } list } checklist && ActivePageId is { } checklistPage
                ? list.Items.Select((item, index) => new BoardsChecklistRow(this, checklistPage, checklist.Id, item.Id, item.Text, item.Checked,
                    list.Kind == NotesListKind.Numbered ? $"{index + 1}." : "•")).ToArray() : [],
            "CanMovePageUp" => CanMovePage(-1),
            "CanMovePageDown" => CanMovePage(1),
            "CanDeletePage" => AllowRetainedRichInput && ActiveSection?.Pages.Count(page => !BoardsWorkspaceService.IsPageDeleted(_document!, page.Id)) > 1,
            "DetailsLabel" => _showDetails ? "Hide details" : "Storage and capabilities",
            "StorageLabel" => "Saved in this browser",
            "StorageDetail" => "Boards on this device use local storage. Account sign-in does not sync them. Keep browser data to retain these copies.",
            "CapabilitiesDetail" => "Native text, headings, tables, checklists and page recovery use the shared editor. Compatible native hosts also support shared inline and page drawing with whole-stroke erasing. Split erasing, nested pages, shared Data views and Chat pages are not connected here yet. Existing unsupported content is preserved.",
            "CanRestorePage" => CanChangeBoard,
            "IsViewMode" => ActivePageId is { } viewId && _document is not null && (IsReadOnly || _boards.GetEditMode(_document, viewId) == BoardsPageEditMode.View),
            _ => null
        };
        return path is "WorkspaceColumns" or "EditorColumn" or "IsCompact" or "ShowSidebar" or "DrawerWidth" or "HasBoard" or "HasNoBoard" or "HasBoards" or "NoBoards" or "BoardTitle" or "PageTitle" or "Breadcrumb"
            or "BoardSearch" or "ShowBoardPicker" or "ShowPageTree" or "ShowTrash" or "ShowDetails" or "CanChangeBoard"
            or "CanEditPage" or "IsEditMode" or "LayoutUnlocked" or "EditModeLabel" or "PageRows" or "DeletedPages"
            or "HasDeletedPages" or "PagePosition" or "SelectionLabel" or "IsChecklistSelected" or "HasUnsupportedPageContent" or "ChecklistItems" or "CanMovePageUp" or "CanMovePageDown" or "CanDeletePage"
            or "PageInkCanvas" or "SharedInkAvailable" or "SharedInkUnavailable" or "HasSharedDrawings" or "HasSelectedSharedDrawing" or "SharedDrawingRows"
            or "DetailsLabel" or "StorageLabel" or "StorageDetail" or "CapabilitiesDetail" or "CanRestorePage" or "IsViewMode";
    }

    public bool TrySetValue(string path, object? value)
    {
        if (path == "BoardSearch" && value is string search) { _boardSearch = search; Notify(); return true; }
        if (!CanChangeBoard) return false;
        if (path == "IsEditMode" && value is bool edit && ActivePageId is { } editId)
        {
            var next = edit ? BoardsPageEditMode.Edit : BoardsPageEditMode.View;
            if (_boards.GetEditMode(_document!, editId) == next) return true;
            _boards.SetEditMode(_document!, editId, next);
            // Undo must never restore a prior mode and grant authoring implicitly.
            RebindActiveEditor(); MarkWorkspaceDirty(); return true;
        }
        if (!AllowRetainedRichInput) return false;
        if (path == "LayoutUnlocked" && value is bool unlocked && ActivePageId is { } layoutId)
        {
            var next = unlocked ? BoardsPageLayoutMode.Unlocked : BoardsPageLayoutMode.Locked;
            if (_boards.GetLayoutMode(_document!, layoutId) == next) return true;
            _boards.SetLayoutMode(_document!, layoutId, next); RebindActiveEditor(); MarkWorkspaceDirty(); return true;
        }
        if (path == "PageTitle" && value is string pageTitle && ActivePageId is { } pageId && pageTitle != ActivePage?.Title)
        {
            _boards.RenamePage(_document!, pageId, LimitTitle(pageTitle)); RebindActiveEditor(); MarkWorkspaceDirty(); return true;
        }
        if (path == "BoardTitle" && value is string boardTitle && !string.IsNullOrWhiteSpace(boardTitle))
        { _richEditor!.SetTitle(LimitTitle(boardTitle)); return true; }
        return false;
    }

    private static string LimitTitle(string title) => title.Length > 256 ? title[..256] : title;
    private void MarkWorkspaceDirty() { ++_generation; IsDirty = true; Status = "Unsaved changes"; Notify(); }
    private void RebindActiveEditor()
    {
        var selected = _richEditor?.SelectedBlockId;
        UnbindRichEditor(); BindRichEditor();
        if (selected is { } id) _richEditor?.SelectBlock(id);
    }

    internal bool UpdateChecklist(Guid pageId, Guid blockId, Guid itemId, string? text = null, bool? check = null)
    {
        if (!AllowRetainedRichInput || ActivePageId != pageId || _richEditor?.SelectedBlockId != blockId) return false;
        if (_richEditor.SelectedBlock?.List?.Items.Any(item => item.Id == itemId) != true) return false;
        if (text is not null) _richEditor.UpdateListItem(itemId, text);
        if (check is { } completed) _richEditor.ToggleListItem(itemId, completed);
        return true;
    }

    public Task<HomeCoreOperationResult<bool>> SelectPageAsync(Guid pageId, CancellationToken token = default) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (_presentationPending || IsHistoryPreview || _pending is not null) return Task.FromResult(Failure<bool>("OperationDenied", "Return to the current board and finish recovery before changing pages."));
        if (!VisiblePages.Any(page => page.Id == pageId)) return Task.FromResult(Failure<bool>("PageNotFound", "This page is not available in the current board."));
        if (pageId != ActivePageId) { ActivePageId = pageId; RebindActiveEditor(); }
        _drawerOpen = false;
        return Task.FromResult(Success(true));
    }, token);

    public Task<HomeCoreOperationResult<bool>> RenamePageAsync(Guid pageId, string title, CancellationToken token = default) =>
        Edit((owner, document) => owner.RenamePage(document, pageId, LimitTitle(title)), token);
    public Task<HomeCoreOperationResult<bool>> DeletePageAsync(Guid pageId, CancellationToken token = default) =>
        Edit((owner, document) => owner.DeletePage(document, pageId), token);
    public Task<HomeCoreOperationResult<bool>> RestorePageAsync(Guid pageId, CancellationToken token = default) =>
        Edit((owner, document) => owner.RestorePage(document, pageId), token);
    public Task<HomeCoreOperationResult<bool>> MovePageAsync(Guid pageId, Guid sectionId, int index, CancellationToken token = default) =>
        Edit((owner, document) => { owner.MovePage(document, pageId, sectionId, index); return true; }, token);

    private bool CanMovePage(int delta)
    {
        if (!AllowRetainedRichInput || ActiveSection is not { } section || ActivePageId is not { } pageId) return false;
        var pages = section.Pages.Where(page => !BoardsWorkspaceService.IsPageDeleted(_document!, page.Id)).OrderBy(page => page.Order).ToArray();
        var target = Array.FindIndex(pages, page => page.Id == pageId) + delta;
        return target >= 0 && target < pages.Length;
    }

    private bool IsWorkspaceActionAvailable(string command) => command switch
    {
        "ToggleBoardPicker" or "ToggleDetails" or "ToggleSidebar" => true,
        "SelectPage" or "ToggleTrash" => _document is not null,
        "AddPage" or "AddSection" => CanChangeBoard && RichEditingPermitted,
        "DeletePage" => CanChangeBoard && RichEditingPermitted && ActiveSection?.Pages.Count(page => !BoardsWorkspaceService.IsPageDeleted(_document!, page.Id)) > 1,
        "RestorePage" => CanChangeBoard,
        "MovePageUp" => CanMovePage(-1), "MovePageDown" => CanMovePage(1),
        "Italic" or "Underline" or "Strikethrough" => RichEditingPermitted,
        "AddChecklistItem" => RichEditingPermitted && _richEditor?.SelectedBlock?.List is not null,
        "InsertSharedDrawing" => SharedInkAvailable && RichEditingPermitted,
        "SelectSharedDrawing" => _document is not null && !IsHistoryPreview,
        _ => IsTableActionAvailable(command) || IsNoteBlockActionAvailable(command)
    };

    private async Task<bool> DispatchWorkspaceAsync(string command, object? parameter, CancellationToken token)
    {
        if (await DispatchTableAsync(command, token)) return true;
        if (await DispatchNoteBlockAsync(command, token)) return true;
        switch (command)
        {
            case "ToggleSidebar": _drawerOpen = !_drawerOpen; Notify(); return true;
            case "ToggleBoardPicker": _showPicker = !_showPicker; Notify(); return true;
            case "ToggleTrash": _showTrash = !_showTrash; Notify(); return true;
            case "ToggleDetails": _showDetails = !_showDetails; Notify(); return true;
            case "SelectPage" when parameter is BoardsPageRow row: await SelectPageAsync(row.Id, token); return true;
            case "RestorePage" when parameter is BoardsPageRow row:
                if ((await RestorePageAsync(row.Id, token)).Value) { await SelectPageAsync(row.Id, token); _showTrash = false; Notify(); } return true;
            case "DeletePage" when ActivePageId is { } pageId: await DeletePageAsync(pageId, token); return true;
            case "AddPage" when ActiveSection is { } section:
                var added = await AddPageAsync(section.Id, token: token);
                if (added.Succeeded) await SelectPageAsync(added.Value, token); return true;
            case "AddSection":
                var group = await AddSectionAsync(token: token);
                if (group.Succeeded && _document!.Sections.First(item => item.Id == group.Value).Pages.FirstOrDefault() is { } first)
                    await SelectPageAsync(first.Id, token); return true;
            case "MovePageUp": case "MovePageDown":
                if (ActivePageId is { } moveId && ActiveSection is { } target)
                {
                    var visible = target.Pages.Where(page => !BoardsWorkspaceService.IsPageDeleted(_document!, page.Id)).OrderBy(page => page.Order).ToArray();
                    var index = Array.FindIndex(visible, page => page.Id == moveId) + (command == "MovePageUp" ? -1 : 1);
                    if (index >= 0 && index < visible.Length) await MovePageAsync(moveId, target.Id, target.Pages.IndexOf(visible[index]), token);
                }
                return true;
            case "Italic": await RichOperation(editor => { editor.ToggleSelectionCharacter(WriteCharacterFormat.Italic); return true; }, token); return true;
            case "Underline": await RichOperation(editor => { editor.ToggleSelectionCharacter(WriteCharacterFormat.Underline); return true; }, token); return true;
            case "Strikethrough": await RichOperation(editor => { editor.ToggleSelectionCharacter(WriteCharacterFormat.StrikeThrough); return true; }, token); return true;
            case "AddChecklistItem": await RichOperation(editor => { editor.AddListItem(); return true; }, token); return true;
            case "InsertSharedDrawing": await InsertSharedDrawingAsync(token); return true;
            case "SelectSharedDrawing" when parameter is BoardsDrawingRow drawing:
                await SelectSharedDrawingAsync(drawing.Id, token); return true;
            default: return false;
        }
    }
    private static bool IsSupportedNoteBlock(NotesBlockKind kind) => kind is NotesBlockKind.Paragraph or NotesBlockKind.Heading
        or NotesBlockKind.Quote or NotesBlockKind.Code or NotesBlockKind.List or NotesBlockKind.Table or NotesBlockKind.Equation or NotesBlockKind.Divider;

    /// <summary>Insert through the existing shared editor after the intended
    /// selected original block. No app-local block model or history is created.</summary>
    public Task<HomeCoreOperationResult<Guid>> InsertNoteBlockAsync(Guid pageId, Guid? afterBlockId, NotesBlockKind kind,
        NotesListKind listKind = NotesListKind.Bulleted, CancellationToken token = default) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (!RichEditingPermitted) return Task.FromResult(Failure<Guid>("OperationDenied", "This page cannot be edited in its current state."));
        var editor = _richEditor!;
        if (ActivePageId != pageId || editor.SelectedBlockId != afterBlockId ||
            afterBlockId is { } blockId && editor.Blocks().All(block => block.Id != blockId))
            return Task.FromResult(Failure<Guid>("StaleBlockSelection", "Select the intended insertion block again."));
        if (!IsSupportedNoteBlock(kind) || !Enum.IsDefined(listKind))
            return Task.FromResult(Failure<Guid>("UnsupportedBlockType", "This shared block type is not supported by this insert action."));
        var added = editor.InsertBlock(kind, listKind);
        // InsertBlock owns mutation/history; selection belongs to the same editor.
        // Retire previous text ranges and select a real cell when inserting tables.
        editor.SelectBlock(added.Id);
        if (added.Table?.Rows.SelectMany(row => row.Cells).FirstOrDefault() is { } firstCell)
            editor.SelectTableCell(added.Id, firstCell.Id);
        return Task.FromResult(Success(added.Id));
    }, token);

    private bool CanChangeSelectedBlock(BoardsBlockEdit edit)
    {
        if (!RichEditingPermitted || _richEditor?.SelectedBlock is not { } block || ActivePage is not { } page) return false;
        var index = page.Blocks.IndexOf(block);
        return edit switch
        {
            BoardsBlockEdit.MoveUp => index > 0,
            BoardsBlockEdit.MoveDown => index >= 0 && index + 1 < page.Blocks.Count,
            BoardsBlockEdit.Delete => index >= 0 && page.Blocks.Count > 1,
            BoardsBlockEdit.BulletedList or BoardsBlockEdit.NumberedList or BoardsBlockEdit.Checklist => block.List is not null,
            _ => false
        };
    }
    public Task<HomeCoreOperationResult<bool>> ChangeNoteBlockAsync(Guid pageId, Guid blockId, BoardsBlockEdit edit,
        CancellationToken token = default) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (!RichEditingPermitted) return Task.FromResult(Failure<bool>("OperationDenied", "This page cannot be edited in its current state."));
        if (ActivePageId != pageId || _richEditor!.SelectedBlockId != blockId)
            return Task.FromResult(Failure<bool>("StaleBlockSelection", "Select this block again before changing it."));
        if (!CanChangeSelectedBlock(edit))
            return Task.FromResult(Failure<bool>("InvalidBlockOperation", "This block action is unavailable at the current position or for this block type."));
        var editor = _richEditor!;
        switch (edit)
        {
            case BoardsBlockEdit.MoveUp: return Task.FromResult(Success(editor.MoveSelected(-1)));
            case BoardsBlockEdit.MoveDown: return Task.FromResult(Success(editor.MoveSelected(1)));
            case BoardsBlockEdit.Delete: return Task.FromResult(Success(editor.DeleteSelected()));
            case BoardsBlockEdit.BulletedList:
                if (editor.SelectedBlock!.List!.Kind == NotesListKind.Bulleted) return Task.FromResult(Success(false));
                editor.SetListKind(NotesListKind.Bulleted); break;
            case BoardsBlockEdit.NumberedList:
                if (editor.SelectedBlock!.List!.Kind == NotesListKind.Numbered) return Task.FromResult(Success(false));
                editor.SetListKind(NotesListKind.Numbered); break;
            case BoardsBlockEdit.Checklist:
                if (editor.SelectedBlock!.List!.Kind == NotesListKind.Checklist) return Task.FromResult(Success(false));
                editor.SetListKind(NotesListKind.Checklist); break;
        }
        return Task.FromResult(Success(true));
    }, token);

    private bool TryGetNoteBlockValue(string path, out object? value)
    {
        if (path is not ("IsListSelected" or "IsTextBlockSelected" or "IsEquationSelected" or "SelectedBlockCaption" or "SelectedEquationEditors"))
        { value = null; return false; }
        var block = _richEditor?.SelectedBlock;
        value = path switch
        {
            "IsListSelected" => block?.List is not null,
            "IsTextBlockSelected" => block?.Kind is NotesBlockKind.Paragraph or NotesBlockKind.Heading or NotesBlockKind.Quote or NotesBlockKind.Code,
            "IsEquationSelected" => block?.Equation is not null,
            "SelectedBlockCaption" => block is null ? "Page" : $"{block.Kind} · block {(ActivePage?.Blocks.IndexOf(block) ?? -1) + 1} of {ActivePage?.Blocks.Count ?? 0}",
            "SelectedEquationEditors" => block?.Equation is { } equation && _document is not null && ActivePageId is { } page
                ? new[] { new BoardsEquationBinding(this, _document, _richEditor!, page, block.Id, equation) }
                : Array.Empty<BoardsEquationBinding>(),
            _ => null
        };
        return true;
    }

    private static BoardsBlockEdit? BlockEditForCommand(string command) => command switch
    {
        "MoveBlockUp" => BoardsBlockEdit.MoveUp, "MoveBlockDown" => BoardsBlockEdit.MoveDown, "DeleteBlock" => BoardsBlockEdit.Delete,
        "ListBullets" => BoardsBlockEdit.BulletedList, "ListNumbered" => BoardsBlockEdit.NumberedList, "ListChecklist" => BoardsBlockEdit.Checklist,
        _ => null
    };
    private static NotesBlockKind? BlockKindForCommand(string command) => command switch
    {
        "InsertParagraph" => NotesBlockKind.Paragraph, "InsertHeading" => NotesBlockKind.Heading,
        "InsertQuote" => NotesBlockKind.Quote, "InsertCode" => NotesBlockKind.Code,
        "InsertBullets" or "InsertNumbered" or "InsertChecklist" => NotesBlockKind.List,
        "InsertTable" => NotesBlockKind.Table, "InsertEquation" => NotesBlockKind.Equation, "InsertDivider" => NotesBlockKind.Divider,
        _ => null
    };
    private bool IsNoteBlockActionAvailable(string command) => BlockKindForCommand(command) is not null ? RichEditingPermitted
        : BlockEditForCommand(command) is { } edit && CanChangeSelectedBlock(edit);

    private async Task<bool> DispatchNoteBlockAsync(string command, CancellationToken token)
    {
        if (BlockKindForCommand(command) is { } kind)
        {
            if (ActivePageId is { } page)
            {
                var listKind = command switch { "InsertChecklist" => NotesListKind.Checklist, "InsertNumbered" => NotesListKind.Numbered, _ => NotesListKind.Bulleted };
                await InsertNoteBlockAsync(page, _richEditor?.SelectedBlockId, kind, listKind, token);
            }
            return true;
        }
        if (BlockEditForCommand(command) is not { } edit) return false;
        if (ActivePageId is { } pageId && _richEditor?.SelectedBlockId is { } blockId)
            await ChangeNoteBlockAsync(pageId, blockId, edit, token);
        return true;
    }

    private bool IsCurrentEquationBinding(BoardsEquationBinding binding) =>
        ReferenceEquals(binding.Owner, this) && ReferenceEquals(binding.Document, _document) &&
        ReferenceEquals(binding.Editor, _richEditor) && ActivePageId == binding.PageId &&
        _richEditor?.SelectedBlockId == binding.BlockId && ReferenceEquals(_richEditor?.SelectedBlock?.Equation, binding.Equation);
    internal bool CanEditEquationBinding(BoardsEquationBinding binding) => AllowRetainedRichInput && IsCurrentEquationBinding(binding);
    internal bool UpdateEquationBinding(BoardsEquationBinding binding, string? source = null, string? alternative = null)
    {
        if (!CanEditEquationBinding(binding)) return false;
        _richEditor!.UpdateEquation(source ?? binding.Equation.Source, alternative ?? binding.Equation.AccessibleAlternative);
        return true;
    }

    public Task<HomeCoreOperationResult<bool>> UpdateNoteEquationAsync(Guid pageId, Guid blockId, string source,
        string alternative, CancellationToken token = default) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (!RichEditingPermitted) return Task.FromResult(Failure<bool>("OperationDenied", "This page cannot be edited in its current state."));
        var editor = _richEditor!;
        if (ActivePageId != pageId || editor.SelectedBlockId != blockId || editor.SelectedBlock?.Equation is null)
            return Task.FromResult(Failure<bool>("StaleBlockSelection", "Select this equation again before editing it."));
        if (source is null || alternative is null) return Task.FromResult(Failure<bool>("InvalidArgument", "Provide equation source and accessible text."));
        var equation = editor.SelectedBlock!.Equation!;
        if (equation.Source == source && equation.AccessibleAlternative == alternative)
            return Task.FromResult(Success(false));
        editor.UpdateEquation(source, alternative); return Task.FromResult(Success(true));
    }, token);

    // These controls borrow the SAME Notes table and shared editor. They own no
    // grid, values, persistence counter or second history stack.
    private sealed record TableCellContext(NotesTableData Table, NotesTableRow Row,
        NotesTableCell Cell, int RowIndex, int ColumnIndex);

    private TableCellContext? CurrentTableCell()
    {
        if (_richEditor?.SelectedBlock?.Table is not { } table || _richEditor.SelectedTableCellId is not { } id) return null;
        for (var row = 0; row < table.Rows.Count; row++)
            for (var column = 0; column < table.Rows[row].Cells.Count; column++)
                if (table.Rows[row].Cells[column].Id == id)
                    return new(table, table.Rows[row], table.Rows[row].Cells[column], row, column);
        return null;
    }

    private bool TryGetTableValue(string path, out object? value)
    {
        if (path is not ("IsTableSelected" or "HasSelectedTableCell" or "TableShapeLabel" or "TableStructureHint" or "SelectedTableCellEditors"))
        { value = null; return false; }
        var table = _richEditor?.SelectedBlock?.Table;
        var cell = CurrentTableCell();
        value = path switch
        {
            "IsTableSelected" => table is not null,
            "HasSelectedTableCell" => cell is not null,
            "TableShapeLabel" => table is null ? string.Empty : $"Table · {table.Rows.Count} rows · {table.Rows.FirstOrDefault()?.Cells.Sum(item => (long)item.ColumnSpan) ?? 0} columns",
            "TableStructureHint" => table is null ? string.Empty : IsRegularTable(table)
                ? "Row and column changes apply beside the selected cell."
                : "Row and column changes need regular, unmerged rows. Existing content is preserved.",
            "SelectedTableCellEditors" => cell is not null && _document is not null && ActivePageId is { } page && _richEditor?.SelectedBlockId is { } block
                ? new[] { new BoardsTableCellBinding(this, _document, _richEditor!, page, block, cell.Cell,
                    $"Row {cell.RowIndex + 1}, column {cell.Row.Cells.Take(cell.ColumnIndex).Sum(item => item.ColumnSpan) + 1}") }
                : Array.Empty<BoardsTableCellBinding>(),
            _ => null
        };
        return path is "IsTableSelected" or "HasSelectedTableCell" or "TableShapeLabel" or "TableStructureHint" or "SelectedTableCellEditors";
    }

    private static bool IsHorizontalTable(NotesTableData table)
    {
        if (table.Rows.Count is 0 or > 10_000) return false;
        var width = table.Rows[0].Cells.Sum(cell => (long)cell.ColumnSpan);
        return width is > 0 and <= 500 && table.Rows.All(row => row.Cells.Count > 0 &&
            row.Cells.All(cell => cell.RowSpan == 1 && cell.ColumnSpan is >= 1 and <= 500) &&
            row.Cells.Sum(cell => (long)cell.ColumnSpan) == width);
    }
    private static bool IsRegularTable(NotesTableData table) => IsHorizontalTable(table) &&
        table.Rows.All(row => row.Cells.All(cell => cell.ColumnSpan == 1));

    private static bool CanEditTable(TableCellContext context, BoardsTableEdit edit) => edit switch
    {
        BoardsTableEdit.Text or BoardsTableEdit.Fill => true,
        BoardsTableEdit.AddRowAfter => IsRegularTable(context.Table) && context.Table.Rows.Count < 10_000,
        BoardsTableEdit.AddColumnAfter => IsRegularTable(context.Table) && context.Row.Cells.Count < 500,
        BoardsTableEdit.RemoveRow => IsRegularTable(context.Table) && context.Table.Rows.Count > 1,
        BoardsTableEdit.RemoveColumn => IsRegularTable(context.Table) && context.Row.Cells.Count > 1,
        BoardsTableEdit.MergeRight => IsHorizontalTable(context.Table) && context.ColumnIndex + 1 < context.Row.Cells.Count,
        BoardsTableEdit.Split => IsHorizontalTable(context.Table) && context.Cell.ColumnSpan > 1,
        _ => false
    };

    /// <summary>Select an original shared cell without changing notebook content.</summary>
    public Task<HomeCoreOperationResult<bool>> SelectTableCellAsync(Guid pageId, Guid blockId, Guid cellId,
        CancellationToken token = default) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (_presentationPending || IsHistoryPreview || _pending is not null || _richEditor is null || ActivePageId != pageId)
            return Task.FromResult(Failure<bool>("OperationDenied", "Return to the current page before selecting a table cell."));
        var block = _richEditor.Blocks().FirstOrDefault(item => item.Id == blockId);
        if (block?.Table?.Rows.SelectMany(row => row.Cells).Count(cell => cell.Id == cellId) != 1)
            return Task.FromResult(Failure<bool>("BlockNotFound", "This table cell is not available on the current page."));
        _richEditor.SelectTableCell(blockId, cellId); NotifySelectionChanged();
        return Task.FromResult(Success(true));
    }, token);

    /// <summary>Typed table action over the existing page-scoped shared editor.
    /// Stable target IDs must still name the selected cell when execution starts.</summary>
    public Task<HomeCoreOperationResult<bool>> EditTableAsync(Guid pageId, Guid blockId, Guid cellId,
        BoardsTableEdit edit, string? value = null, CancellationToken token = default) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (!RichEditingPermitted)
            return Task.FromResult(Failure<bool>("OperationDenied", "This page cannot be edited in its current state."));
        var editor = _richEditor!;
        if (ActivePageId != pageId || editor.SelectedBlockId != blockId || CurrentTableCell() is not { } context || context.Cell.Id != cellId)
            return Task.FromResult(Failure<bool>("StaleTableSelection", "Select this table cell again before editing it."));
        if (!CanEditTable(context, edit))
            return Task.FromResult(Failure<bool>("InvalidTableOperation", "This action is unavailable for the current table structure. Split merged cells before changing rows or columns."));
        switch (edit)
        {
            case BoardsTableEdit.Text:
                if (value is null) return Task.FromResult(Failure<bool>("InvalidArgument", "Provide the cell text."));
                if (context.Cell.Text == value) return Task.FromResult(Success(false));
                editor.UpdateTableCell(cellId, value); break;
            case BoardsTableEdit.Fill:
                if (value is null || value.Length is not (7 or 9) || value[0] != '#' || !value.Skip(1).All(Uri.IsHexDigit))
                    return Task.FromResult(Failure<bool>("InvalidArgument", "Use a six- or eight-digit hexadecimal cell fill."));
                return Task.FromResult(Success(editor.SetTableCellBackground(cellId, value)));
            case BoardsTableEdit.AddRowAfter: editor.AddTableRow(); break;
            case BoardsTableEdit.AddColumnAfter: editor.AddTableColumn(); break;
            case BoardsTableEdit.RemoveRow: editor.RemoveTableRow(); break;
            case BoardsTableEdit.RemoveColumn: editor.RemoveTableColumn(); break;
            case BoardsTableEdit.MergeRight: return Task.FromResult(Success(editor.MergeTableCellRight()));
            case BoardsTableEdit.Split: return Task.FromResult(Success(editor.SplitTableCell()));
        }
        return Task.FromResult(Success(true));
    }, token);

    private static BoardsTableEdit? TableEditForCommand(string command) => command switch
    {
        "TableAddRow" => BoardsTableEdit.AddRowAfter, "TableAddColumn" => BoardsTableEdit.AddColumnAfter,
        "TableRemoveRow" => BoardsTableEdit.RemoveRow, "TableRemoveColumn" => BoardsTableEdit.RemoveColumn,
        "TableMergeRight" => BoardsTableEdit.MergeRight, "TableSplit" => BoardsTableEdit.Split,
        "TableFillNone" or "TableFillBlue" or "TableFillAmber" or "TableFillGreen" => BoardsTableEdit.Fill,
        _ => null
    };
    private bool IsTableActionAvailable(string command)
    {
        if (command == "TableFirstCell") return AllowRetainedSelection && _richEditor?.SelectedBlock?.Table?.Rows.Any(row => row.Cells.Count > 0) == true;
        if (command is "TablePreviousCell" or "TableNextCell")
        {
            if (!AllowRetainedSelection || CurrentTableCell() is not { } cell) return false;
            return command == "TablePreviousCell" ? cell.RowIndex > 0 || cell.ColumnIndex > 0
                : cell.ColumnIndex + 1 < cell.Row.Cells.Count || cell.RowIndex + 1 < cell.Table.Rows.Count;
        }
        return RichEditingPermitted && TableEditForCommand(command) is { } edit && CurrentTableCell() is { } context && CanEditTable(context, edit);
    }
    private async Task<bool> DispatchTableAsync(string command, CancellationToken token)
    {
        if (command is "TableFirstCell" or "TablePreviousCell" or "TableNextCell")
        {
            if (ActivePageId is not { } page || _richEditor?.SelectedBlock is not { Table: { } table } block) return true;
            NotesTableCell? target;
            if (command == "TableFirstCell") target = table.Rows.SelectMany(row => row.Cells).FirstOrDefault();
            else if (CurrentTableCell() is { } context)
            {
                var next = command == "TableNextCell";
                target = next ? context.ColumnIndex + 1 < context.Row.Cells.Count ? context.Row.Cells[context.ColumnIndex + 1]
                        : context.Table.Rows.Skip(context.RowIndex + 1).SelectMany(row => row.Cells).FirstOrDefault()
                    : context.ColumnIndex > 0 ? context.Row.Cells[context.ColumnIndex - 1]
                        : context.Table.Rows.Take(context.RowIndex).SelectMany(row => row.Cells).LastOrDefault();
            }
            else target = null;
            if (target is not null) await SelectTableCellAsync(page, block.Id, target.Id, token);
            return true;
        }
        if (TableEditForCommand(command) is not { } edit) return false;
        if (ActivePageId is { } pageId && _richEditor?.SelectedBlockId is { } blockId && CurrentTableCell() is { } cell)
        {
            var fill = command switch { "TableFillNone" => "#00000000", "TableFillBlue" => "#335C93", "TableFillAmber" => "#7B5B2A", "TableFillGreen" => "#315F4C", _ => null };
            await EditTableAsync(pageId, blockId, cell.Cell.Id, edit, fill, token);
        }
        return true;
    }

    internal bool IsCurrentTableBinding(BoardsTableCellBinding binding) =>
        ReferenceEquals(binding.Owner, this) && ReferenceEquals(binding.Document, _document) &&
        ReferenceEquals(binding.Editor, _richEditor) && ActivePageId == binding.PageId &&
        _richEditor?.SelectedBlockId == binding.BlockId && CurrentTableCell() is { } context && ReferenceEquals(context.Cell, binding.Cell);

    internal bool UpdateTableBinding(BoardsTableCellBinding binding, string text)
    {
        // Synchronous CUI callbacks cannot race a save/owner operation, cannot
        // follow an undo/rebind, and never redirect a stale field to a new cell.
        if (!AllowRetainedRichInput || !IsCurrentTableBinding(binding)) return false;
        _richEditor!.UpdateTableCell(binding.Cell.Id, text); return true;
    }

    internal string? PageInkDiagnostic { get; private set; }
    public BoardsPageSharedInkSnapshot? CapturePageSharedDrawing()
    {
        PageInkDiagnostic = null;
        if (_disposed || _presentationPending || IsHistoryPreview || _pending is not null || _richEditor is null || ActivePageId is not { } page) return null;
        try
        {
            var content = _richEditor.CapturePageSharedInk(page);
            return content is null ? null : new(content, _generation, _sharedInkBindingEpoch, _sharedInkSelectionEpoch);
        }
        catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or NotSupportedException)
        {
            PageInkDiagnostic = error.Message;
            return null; // Preserve incompatible original data and keep unrelated page editing available.
        }
    }
    private bool IsPageDrawingCurrent(BoardsPageSharedInkSnapshot expected) =>
        RichEditingPermitted && _sharedInkAvailable && _richEditor is { } editor && ActivePageId == expected.Content.Page.Id &&
        _generation == expected.Generation && _sharedInkBindingEpoch == expected.BindingEpoch &&
        _sharedInkSelectionEpoch == expected.SelectionEpoch && editor.IsPageSharedInkCurrent(expected.Content);

    public Task<HomeCoreOperationResult<bool>> ReplacePageSharedDrawingAsync(BoardsPageSharedInkSnapshot expected,
        IReadOnlyList<System.Text.Json.JsonElement> proposed,
        Func<IReadOnlyList<System.Text.Json.JsonElement>, IReadOnlyList<System.Text.Json.JsonElement>> captureCanonical,
        CancellationToken token = default, double? width = null, double? height = null) => Operate(async ct =>
    {
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(captureCanonical);
        ct.ThrowIfCancellationRequested();
        if (!IsPageDrawingCurrent(expected)) return Failure<bool>("StaleDrawing", "The page, drawing or authoring context changed.");
        IReadOnlyList<System.Text.Json.JsonElement> Capture(IReadOnlyList<System.Text.Json.JsonElement> source)
        {
            var captured = captureCanonical(source); ct.ThrowIfCancellationRequested();
            if (!IsPageDrawingCurrent(expected)) throw new SharedDrawingStaleException();
            return captured;
        }
        try
        {
            if (!_richEditor!.ReplacePageSharedInk(expected.Content, proposed, Capture, width, height))
                return Failure<bool>("StaleDrawing", "The original page drawing changed before insertion.");
        }
        catch (SharedDrawingStaleException) { return Failure<bool>("StaleDrawing", "The page changed during drawing validation."); }
        catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or NotSupportedException)
        { return Failure<bool>("SharedEngineIncompatible", error.Message); }
        return await SaveCore(ct); // Failed/unknown acknowledgement retains the same undoable draft and CAS fences.
    }, token);

}

public sealed record BoardsChecklistRow(BoardsBrowserCore Owner, Guid PageId, Guid BlockId, Guid Id, string Text, bool Checked, string Marker = "•") : ICuiWritableBindingContext
{
    private readonly WriteDocumentEditor? _editor = Owner.NativeRichEditor;
    private readonly NotesListItem? _item = Owner.NativeRichEditor?.SelectedBlock?.List?.Items.FirstOrDefault(item => item.Id == Id);
    private bool IsCurrent => _item is not null && ReferenceEquals(_editor, Owner.NativeRichEditor) && Owner.ActivePageId == PageId &&
        Owner.NativeRichEditor?.SelectedBlockId == BlockId && ReferenceEquals(_item,
            Owner.NativeRichEditor?.SelectedBlock?.List?.Items.FirstOrDefault(item => item.Id == Id));
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch { "Id" => Id, "Text" => _item?.Text ?? Text, "Checked" => _item?.Checked ?? Checked,
            "Marker" => Marker, "CanEdit" => Owner.AllowRetainedRichInput && IsCurrent, _ => null };
        return path is "Id" or "Text" or "Checked" or "Marker" or "CanEdit";
    }
    public bool TrySetValue(string path, object? value) => IsCurrent && (path switch
    {
        "Text" when value is string text => Owner.UpdateChecklist(PageId, BlockId, Id, text: text),
        "Checked" when value is bool check => Owner.UpdateChecklist(PageId, BlockId, Id, check: check),
        _ => false
    });
}

public sealed record BoardsPageRow(Guid Id, string Title, string Section, bool Selected) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch { "Id" => Id, "Title" => Title, "Section" => Section, "Selected" => Selected,
            "Label" => (Selected ? "●  " : "▤  ") + Title, _ => null };
        return path is "Id" or "Title" or "Section" or "Selected" or "Label";
    }
}

public sealed record BoardsDrawingRow(Guid Id, string Label) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    { value = path switch { "Id" => Id, "Label" => Label, _ => null }; return path is "Id" or "Label"; }
}

public sealed record BoardsPageSharedInkSnapshot(WritePageSharedInkSnapshot Content, long Generation,
    long BindingEpoch, long SelectionEpoch);

public enum BoardsTableEdit { Text, AddRowAfter, AddColumnAfter, RemoveRow, RemoveColumn, MergeRight, Split, Fill }

/// <summary>A live CUI field for one original selected cell, not a table copy.
/// Editor/document/cell reference checks retire callbacks after rebind or undo.</summary>
public sealed class BoardsTableCellBinding : ICuiWritableBindingContext
{
    internal BoardsBrowserCore Owner { get; }
    internal NotesDocument Document { get; }
    internal WriteDocumentEditor Editor { get; }
    internal Guid PageId { get; }
    internal Guid BlockId { get; }
    internal NotesTableCell Cell { get; }
    private readonly string _label;
    internal BoardsTableCellBinding(BoardsBrowserCore owner, NotesDocument document, WriteDocumentEditor editor,
        Guid pageId, Guid blockId, NotesTableCell cell, string label)
    { Owner = owner; Document = document; Editor = editor; PageId = pageId; BlockId = blockId; Cell = cell; _label = label; }
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Id" => Cell.Id, "Text" => Cell.Text, "Label" => _label,
            "CanEdit" => Owner.AllowRetainedRichInput && Owner.IsCurrentTableBinding(this),
            _ => null
        };
        return path is "Id" or "Text" or "Label" or "CanEdit";
    }
    public bool TrySetValue(string path, object? value) => path == "Text" && value is string text && Owner.UpdateTableBinding(this, text);
}

public enum BoardsBlockEdit { MoveUp, MoveDown, Delete, BulletedList, NumberedList, Checklist }

/// <summary>Live CUI fields over the selected original shared equation.</summary>
public sealed class BoardsEquationBinding : ICuiWritableBindingContext
{
    internal BoardsBrowserCore Owner { get; }
    internal NotesDocument Document { get; }
    internal WriteDocumentEditor Editor { get; }
    internal Guid PageId { get; }
    internal Guid BlockId { get; }
    internal NotesEquationData Equation { get; }
    internal BoardsEquationBinding(BoardsBrowserCore owner, NotesDocument document, WriteDocumentEditor editor,
        Guid pageId, Guid blockId, NotesEquationData equation)
    { Owner = owner; Document = document; Editor = editor; PageId = pageId; BlockId = blockId; Equation = equation; }
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Id" => BlockId, "Source" => Equation.Source, "Alternative" => Equation.AccessibleAlternative,
            "CanEdit" => Owner.CanEditEquationBinding(this), _ => null
        };
        return path is "Id" or "Source" or "Alternative" or "CanEdit";
    }
    public bool TrySetValue(string path, object? value) => value is string text && (path switch
    {
        "Source" => Owner.UpdateEquationBinding(this, source: text),
        "Alternative" => Owner.UpdateEquationBinding(this, alternative: text),
        _ => false
    });
}
