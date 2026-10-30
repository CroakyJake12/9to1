using CakeOS.Apps.Boards.Contract;
using Haven.Core;

namespace CakeOS.Apps.Boards.App;

public sealed partial class BoardsViewModel
{
    private BoardsDatabaseBlocks? _databaseBlocks;
    private volatile HavenBoardDatabaseReference? _pendingDatabase;
    private string? _pendingDatabasePage, _pendingDatabaseAnchor, _pendingDatabaseBlock;
    private volatile bool _creatingDatabase;
    public BoardsDatabaseBlocks DatabaseBlocks => _databaseBlocks ??= new(this, _embeddedArtifactHost as IBoardsDatabaseSource);
    public bool HasPendingDatabaseLink => _pendingDatabase is not null;
    internal bool HasUnsettledDatabaseWork => _creatingDatabase || HasPendingDatabaseLink ||
        _databaseBlocks?.HasUnsavedDrafts == true || _tabbers?.HasUnsavedDrafts == true;
    private bool AllowDatabaseStructureChange()
    {
        if (!HasUnsettledDatabaseWork) return true;
        Set("StatusText", "Finish pending database creation or links, and save or discard edited fields before deleting content or changing Board history.");
        return false;
    }

    internal async Task<IBoardsPresentationClosePreparation?> PrepareDatabaseCloseAsync(CancellationToken token)
    {
        if (_creatingDatabase || HasPendingDatabaseLink)
        {
            Set("StatusText", _creatingDatabase
                ? "Wait for the current database creation or link to finish before closing."
                : "The database exists in Data, but its Board link is not saved. Return to its original page and choose Link created database before closing.");
            return null;
        }
        var own = _databaseBlocks is null ? CompletedBoardsPresentationClosePreparation.Instance
            : await _databaseBlocks.PrepareCloseAsync(token);
        if (own is null) return null;
        if (_tabbers is null) return own;
        var panes = await _tabbers.PrepareAsync(token);
        if (panes is null) { await own.AbortAsync(); return null; }
        return new CombinedBoardsClosePreparation(own, panes);
    }
    internal async ValueTask DisposeDatabasesAsync()
    {
        if (_tabbers is not null) await _tabbers.DisposeAsync();
        if (_databaseBlocks is not null) await _databaseBlocks.DisposeAsync();
    }

    internal async Task InsertDatabaseAsync(string command, string? after)
    {
        try { await InsertDatabaseCoreAsync(command, after); }
        catch (Exception error) { Set("StatusText", "Database action failed: " + error.Message); }
    }

    private async Task InsertDatabaseCoreAsync(string command, string? after)
    {
        if (command == "database-retry" && _pendingDatabaseDestination is not null)
        { await RetryRelativeDatabaseAsync(); return; }
        if (!RequireEditMode() || _creatingDatabase) return;
        if (!DatabaseBlocks.IsAvailable)
        { Set("StatusText", "The Data owner is unavailable in this host."); return; }
        var kind = command == "database-timeline" ? DataRecordViewKind.Timeline : DataRecordViewKind.Table;
        var target = FindBlock(after ?? SelectedBlockId);
        if (command != "database" && command != "database-retry" && target?.Database is { } existing)
        {
            var selected = await DatabaseBlocks.SelectKindAsync(existing, kind);
            UpdateDatabaseReference(SelectedPageId, target.Id, selected);
            RequestRebuild();
            return;
        }
        if (command != "database-retry")
        {
            if (_pendingDatabase is not null)
            { Set("StatusText", "Link the previously created database before creating another."); return; }
            _creatingDatabase = true;
            _pendingDatabasePage = SelectedPageId;
            _pendingDatabaseAnchor = after ?? SelectedBlockId;
            try { _pendingDatabase = await DatabaseBlocks.CreateAsync(kind); }
            finally { _creatingDatabase = false; }
        }
        if (_pendingDatabase is not { } reference) return;
        if (!RequireEditMode()) return;
        if (_pendingDatabasePage != SelectedPageId)
        { Set("StatusText", "The database was created in Data. Return to its original page and choose Link created database."); return; }
        var page = CurrentPage();
        var index = page.Blocks.FindIndex(block => block.Id == _pendingDatabaseAnchor);
        if (_pendingDatabaseAnchor is not null && index < 0 && page.Blocks.Count > 0)
        { Set("StatusText", "The database is retained in Data; restore the original insertion point before linking it."); return; }
        var block = _pendingDatabaseBlock is null ? null : page.Blocks.FirstOrDefault(item => item.Id == _pendingDatabaseBlock);
        if (block is null)
        {
            block = new RichBoardBlock { Kind = "database", Database = reference, Width = 640, Height = 360 };
            page.Blocks.Insert(index < 0 ? 0 : index + 1, block);
            _pendingDatabaseBlock = block.Id;
            _selectedBlockId = block.Id;
            PushDocumentToBindings(); _session.MarkDirty(); RequestRebuild();
        }
        _creatingDatabase = true;
        try
        {
            await _session.SaveAsync();
            ClearPendingDatabaseLink();
        }
        catch (Exception error)
        { Set("StatusText", "The database exists in Data. Its Board link needs Retry: " + error.Message); }
        finally { _creatingDatabase = false; }
    }

    internal void UpdateDatabaseReference(string pageID, string blockID, HavenBoardDatabaseReference reference)
    {
        if (!CanEdit || !IsEditMode || SelectedPageId != pageID) return;
        var block = FindBlock(blockID);
        if (block?.Database is not { } previous || previous == reference ||
            previous.StoreID != reference.StoreID || previous.WorkbookID != reference.WorkbookID) return;
        reference.Validate();
        block.Database = reference;
        _session.MarkDirty();
    }
}
