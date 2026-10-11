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
    private bool _compact, _drawerOpen;
    private string _boardSearch = string.Empty;
    public Guid? ActivePageId { get; private set; }
    private IEnumerable<NotesPage> VisiblePages => _document?.Sections.SelectMany(section =>
        section.Pages.OrderBy(page => page.Order)).Where(page => !BoardsWorkspaceService.IsPageDeleted(_document!, page.Id)) ?? [];
    private NotesPage? ActivePage => VisiblePages.FirstOrDefault(page => page.Id == ActivePageId);
    private NotesSection? ActiveSection => _document?.Sections.FirstOrDefault(section => section.Pages.Any(page => page.Id == ActivePageId));
    private bool CanChangeBoard => _document is not null && !_disposed && !_presentationPending && !IsBusy && !IsReadOnly && _pending is null;

    internal void NotifySelectionChanged() => Notify();
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
            "HasUnsupportedPageContent" => ActivePage is { } current && (current.CanvasObjects.Count > 0 || current.Blocks.Any(block =>
                block.Kind is NotesBlockKind.Canvas or NotesBlockKind.HtmlWidget)),
            "ChecklistItems" => _richEditor?.SelectedBlock is { List: { } list } checklist && ActivePageId is { } checklistPage
                ? list.Items.Select(item => new BoardsChecklistRow(this, checklistPage, checklist.Id, item.Id, item.Text, item.Checked)).ToArray() : [],
            "CanMovePageUp" => CanMovePage(-1),
            "CanMovePageDown" => CanMovePage(1),
            "CanDeletePage" => AllowRetainedRichInput && ActiveSection?.Pages.Count(page => !BoardsWorkspaceService.IsPageDeleted(_document!, page.Id)) > 1,
            "DetailsLabel" => _showDetails ? "Hide details" : "Storage and capabilities",
            "StorageLabel" => "Saved in this browser",
            "StorageDetail" => "Boards on this device use local storage. Account sign-in does not sync them. Keep browser data to retain these copies.",
            "CapabilitiesDetail" => "This preview supports native text pages, headings, tables, checklists and page recovery. Nested pages, shared Data views, drawing and Chat pages are not connected here yet. Existing unsupported content is preserved.",
            "CanRestorePage" => CanChangeBoard,
            "IsViewMode" => ActivePageId is { } viewId && _document is not null && (IsReadOnly || _boards.GetEditMode(_document, viewId) == BoardsPageEditMode.View),
            _ => null
        };
        return path is "WorkspaceColumns" or "EditorColumn" or "IsCompact" or "ShowSidebar" or "DrawerWidth" or "HasBoard" or "HasNoBoard" or "HasBoards" or "NoBoards" or "BoardTitle" or "PageTitle" or "Breadcrumb"
            or "BoardSearch" or "ShowBoardPicker" or "ShowPageTree" or "ShowTrash" or "ShowDetails" or "CanChangeBoard"
            or "CanEditPage" or "IsEditMode" or "LayoutUnlocked" or "EditModeLabel" or "PageRows" or "DeletedPages"
            or "HasDeletedPages" or "PagePosition" or "SelectionLabel" or "IsChecklistSelected" or "HasUnsupportedPageContent" or "ChecklistItems" or "CanMovePageUp" or "CanMovePageDown" or "CanDeletePage"
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
        if (_presentationPending || _pending is not null) return Task.FromResult(Failure<bool>("OperationDenied", "Finish recovery before changing pages."));
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
        "Italic" or "Underline" or "InsertParagraph" or "InsertHeading" or "InsertChecklist" or "InsertTable" => RichEditingPermitted,
        "AddChecklistItem" => RichEditingPermitted && _richEditor?.SelectedBlock?.List is not null,
        _ => false
    };

    private async Task<bool> DispatchWorkspaceAsync(string command, object? parameter, CancellationToken token)
    {
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
            case "AddChecklistItem": await RichOperation(editor => { editor.AddListItem(); return true; }, token); return true;
            case "InsertParagraph": case "InsertHeading": case "InsertChecklist": case "InsertTable":
                if (ActivePageId is not { } insertPage) return true;
                var kind = command switch { "InsertHeading" => NotesBlockKind.Heading, "InsertChecklist" => NotesBlockKind.List,
                    "InsertTable" => NotesBlockKind.Table, _ => NotesBlockKind.Paragraph };
                var inserted = await Edit((owner, document) => owner.AddBlock(document, insertPage, kind).Id, token);
                if (inserted.Succeeded) { _richEditor?.SelectBlock(inserted.Value); Notify(); }
                return true;
            default: return false;
        }
    }
}

public sealed record BoardsChecklistRow(BoardsBrowserCore Owner, Guid PageId, Guid BlockId, Guid Id, string Text, bool Checked) : ICuiWritableBindingContext
{
    public bool TryGetValue(string path, out object? value)
    { value = path switch { "Id" => Id, "Text" => Text, "Checked" => Checked, _ => null }; return path is "Id" or "Text" or "Checked"; }
    public bool TrySetValue(string path, object? value) => path switch
    {
        "Text" when value is string text => Owner.UpdateChecklist(PageId, BlockId, Id, text: text),
        "Checked" when value is bool check => Owner.UpdateChecklist(PageId, BlockId, Id, check: check),
        _ => false
    };
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
