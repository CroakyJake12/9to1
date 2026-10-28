using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

public sealed partial class BoardsViewModel
{
    private BoardsTabberBlocks? _tabbers;
    private IBoardsDatabaseSource? _explicitDatabaseSource;
    public BoardsTabberBlocks Tabbers => _tabbers ??= new(this);
    public bool CanAddTabber => IsEditMode && IsNativePage && CurrentPage().TabberOwnerBlockId is null && _session is ContractSessionAdapter;
    internal BoardsViewModel CreatePaneModel(string pageID)
    {
        var child = new BoardsViewModel(_session, _embeddedArtifactHost, databaseSource: _explicitDatabaseSource);
        child.PickFileAsync = PickFileAsync;
        if (!child.SelectPageById(pageID)) throw new InvalidOperationException("The Tabber page is unavailable.");
        child.SetEditMode(_isEditMode);
        return child;
    }
    internal void SyncPaneMode(bool editing) => SetEditMode(editing);

    private async Task AddTabberAsync(string? after)
    {
        if (!CanAddTabber || _session is not ContractSessionAdapter adapter) return;
        var anchor = after ?? SelectedBlockId;
        if (!CurrentPage().Blocks.Any(block => block.Id == anchor)) anchor = null;
        _selectedBlockId = await adapter.AddTabberAsync(SelectedPageId, anchor);
        RefreshAfterEdit();
    }
    internal async Task ChangeTabberAsync(string blockID, string action, string? paneID = null, string? title = null)
    {
        if (!RequireEditMode() || _session is not ContractSessionAdapter adapter) return;
        if (action == "remove" && _tabbers?.HasUnsavedDrafts == true)
        { Set("StatusText", "Save or discard pane field edits before removing a tab."); return; }
        try { await adapter.ChangeTabberAsync(SelectedPageId, blockID, action, paneID, title); RefreshAfterEdit(); }
        catch (Exception error) { Set("StatusText", error.Message); }
    }
}
