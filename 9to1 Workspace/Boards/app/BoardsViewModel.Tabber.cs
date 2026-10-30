using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

public sealed partial class BoardsViewModel
{
    private BoardsTabberBlocks? _tabbers;
    private IBoardsDatabaseSource? _explicitDatabaseSource;
    private BoardsViewModel? _paneOwner;
    private sealed record PaneIdentity(string SectionId, string PageId, string ParentPageId, string OwnerBlockId);
    private PaneIdentity? _paneIdentity;
    private int _paneRetired;
    internal void RetirePane() => Interlocked.Exchange(ref _paneRetired, 1);
    internal bool PaneLifetimeIsCurrent
    {
        get
        {
            if (_paneIdentity is not { } identity) return true;
            if (Volatile.Read(ref _paneRetired) != 0) return false;
            var section = _session.Document.Sections.FirstOrDefault(section => section.Id == identity.SectionId);
            var page = section?.Pages.FirstOrDefault(page => page.Id == identity.PageId);
            var parent = section?.Pages.FirstOrDefault(page => page.Id == identity.ParentPageId);
            var tabs = parent?.Blocks.FirstOrDefault(block => block.Id == identity.OwnerBlockId && block.Kind == "tabber")?.Tabber;
            if (!_disposed && _paneOwner is { _disposed: false } &&
                ReferenceEquals(_session, _paneOwner._session) && SelectedPageId == identity.PageId &&
                page?.TabberOwnerBlockId == identity.OwnerBlockId && page.ParentPageId == identity.ParentPageId &&
                tabs?.Panes.Any(pane => pane.PageId == identity.PageId) == true) return true;
            Interlocked.Exchange(ref _paneRetired, 1);
            return false;
        }
    }
    public BoardsTabberBlocks Tabbers => _tabbers ??= new(this);
    public bool CanAddTabber => CanEdit && IsEditMode && IsNativePage && CurrentPage().TabberOwnerBlockId is null && _session is ContractSessionAdapter;
    internal BoardsViewModel CreatePaneModel(string pageID)
    {
        var child = new BoardsViewModel(_session, _embeddedArtifactHost, databaseSource: _explicitDatabaseSource);
        child.PickFileAsync = PickFileAsync;
        if (!child.SelectPageById(pageID))
        { child.Dispose(); throw new InvalidOperationException("The Tabber page is unavailable."); }
        var page = child.CurrentPage();
        if (page.ParentPageId is null || page.TabberOwnerBlockId is null)
        { child.Dispose(); throw new InvalidOperationException("The page has no Tabber owner."); }
        child._paneIdentity = new(child.SelectedSectionId, pageID, page.ParentPageId, page.TabberOwnerBlockId);
        child._paneOwner = this;
        child.SetEditMode(_isEditMode);
        return child;
    }
    internal void SyncPaneMode(bool editing) => SetEditMode(editing);

    internal Func<bool> CaptureTabberControls(RichBoardBlock block, bool namesMayChange = false)
    {
        var pageId = SelectedPageId;
        var epoch = Interlocked.Read(ref _insertionOperationEpoch);
        var panes = block.Tabber!.Panes.ToArray();
        var selected = block.Tabber.SelectedPaneId;
        return () =>
        {
            var current = _session.Document.Sections.SelectMany(section => section.Pages)
                .FirstOrDefault(page => page.Id == pageId)?.Blocks.FirstOrDefault(item => item.Id == block.Id && item.Kind == "tabber")?.Tabber;
            return !_disposed && !EditorLifetimeEnded && SelectedPageId == pageId &&
                Interlocked.Read(ref _insertionOperationEpoch) == epoch &&
                current is not null && current.SelectedPaneId == selected &&
                (namesMayChange
                    ? current.Panes.Select(pane => (pane.Id, pane.PageId)).SequenceEqual(panes.Select(pane => (pane.Id, pane.PageId)))
                    : current.Panes.SequenceEqual(panes));
        };
    }

    private async Task AddTabberAsync(string? after)
    {
        if (!CanAddTabber || _session is not ContractSessionAdapter adapter) return;
        var anchor = after ?? SelectedBlockId;
        if (!CurrentPage().Blocks.Any(block => block.Id == anchor)) anchor = null;
        _selectedBlockId = await adapter.AddTabberAsync(SelectedPageId, anchor);
        RefreshAfterEdit();
    }
    internal async Task ChangeTabberAsync(string blockID, string action, string? paneID = null, string? title = null,
        Func<bool>? controlsCurrent = null, string? pendingPaneTitle = null)
    {
        if (!RequireEditMode() || _session is not ContractSessionAdapter adapter) return;
        if (action == "remove" && !AllowDatabaseStructureChange()) return;
        var pageId = SelectedPageId;
        var epoch = Interlocked.Read(ref _insertionOperationEpoch);
        bool Current() => !_disposed && !EditorLifetimeEnded && CanEdit && _isEditMode &&
            SelectedPageId == pageId && Interlocked.Read(ref _insertionOperationEpoch) == epoch &&
            (controlsCurrent?.Invoke() ?? true) && (action != "remove" || !HasUnsettledDatabaseWork);
        try
        {
            if (!await adapter.TryChangeTabberAsync(pageId, blockID, action, paneID, title, originStillEditable: Current,
                pendingPaneTitle: pendingPaneTitle)) return;
            if (action == "rename" && pendingPaneTitle is null)
            {
                PushDocumentToBindings();
            }
            else RefreshAfterEdit();
        }
        catch (Exception error) { Set("StatusText", error.Message); }
    }
}
