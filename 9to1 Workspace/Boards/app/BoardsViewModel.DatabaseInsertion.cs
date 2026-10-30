using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Haven.Core;

namespace CakeOS.Apps.Boards.App;

public sealed partial class BoardsViewModel
{
    private sealed record DatabaseDestination(string PageId, string RowId, bool WholeList,
        BlockInsertionPlacement Placement, string Stamp);
    private DatabaseDestination? _pendingDatabaseDestination;

    private bool HasDatabaseDrafts => _databaseBlocks?.HasUnsavedDrafts == true || _tabbers?.HasUnsavedDrafts == true;

    private string? DatabaseDestinationStamp(string pageId, string rowId)
    {
        var page = _session.Document.Sections.SelectMany(section => section.Pages).FirstOrDefault(page => page.Id == pageId);
        if (page is null || !page.Blocks.Any(block => block.Id == rowId)) return null;
        var split = rowId.IndexOf(':');
        var rows = page.Blocks.Where(block => split < 0 ? block.Id == rowId :
            block.Id.StartsWith(rowId[..split] + ":", StringComparison.Ordinal)).ToArray();
        return JsonSerializer.Serialize(new { page.Id, page.ParentPageId, page.TabberOwnerBlockId, page.PageType, rows });
    }

    private async Task InsertDatabaseRelativeAsync(string command, string blockId, BlockInsertionPlacement placement)
    {
        if (!RequireEditMode() || _creatingDatabase || _session is not ContractSessionAdapter adapter) return;
        if (command != "database-retry" && HasPendingDatabaseLink)
        { Set("StatusText", "Link the previously created database before creating another."); return; }
        if (placement == BlockInsertionPlacement.Replace && HasDatabaseDrafts)
        { Set("StatusText", "Save or discard edited database fields before replacing content."); return; }
        var row = PageBlocks.FirstOrDefault(block => block.Id == blockId);
        var whole = row is null;
        row ??= PageBlocks.FirstOrDefault(block => block.Id.StartsWith(blockId + ":", StringComparison.Ordinal));
        if (row is null) return;
        var ticket = adapter.CaptureInsertionTarget(SelectedPageId, row.Id, whole);
        var stamp = DatabaseDestinationStamp(SelectedPageId, row.Id);
        if (ticket is null || stamp is null) return;
        var destination = new DatabaseDestination(SelectedPageId, row.Id, whole, placement, stamp);
        var epoch = Interlocked.Read(ref _insertionOperationEpoch);
        bool Current() => CanEdit && _isEditMode && SelectedPageId == destination.PageId &&
            Interlocked.Read(ref _insertionOperationEpoch) == epoch &&
            (placement != BlockInsertionPlacement.Replace || !HasDatabaseDrafts);
        _creatingDatabase = true;
        try
        {
            if (command != "database-retry")
                _pendingDatabase = await DatabaseBlocks.CreateAsync(command == "database-timeline"
                    ? DataRecordViewKind.Timeline : DataRecordViewKind.Table);
            if (_pendingDatabase is null) return;
            // Retain the artifact even if its original editor/page retired during creation.
            // Only a later explicit recovery action may choose another destination.
            _pendingDatabaseDestination = destination;
            _pendingDatabasePage = destination.PageId;
            if (!Current()) { DatabaseLinkRecovery("The insertion origin changed while creating the database."); return; }
            await PublishRelativeDatabaseAsync(adapter, destination, ticket, Current);
        }
        catch (Exception error) { DatabaseLinkRecovery(error.Message.Split('\n')[0]); }
        finally { _creatingDatabase = false; }
    }

    private async Task RetryRelativeDatabaseAsync()
    {
        if (!RequireEditMode() || _creatingDatabase || _pendingDatabaseDestination is not { } destination ||
            _session is not ContractSessionAdapter adapter) return;
        if (SelectedPageId != destination.PageId)
        { DatabaseLinkRecovery("Return to the original page to retry its link."); return; }
        _creatingDatabase = true;
        try
        {
            if (_pendingDatabaseBlock is not null) { await SavePendingDatabaseLinkAsync(); return; }
            if (DatabaseDestinationStamp(destination.PageId, destination.RowId) != destination.Stamp)
            { DatabaseLinkRecovery("The original target changed. Choose a destination with its inline plus, then Link created database."); return; }
            var ticket = adapter.CaptureInsertionTarget(destination.PageId, destination.RowId, destination.WholeList);
            if (ticket is null) { DatabaseLinkRecovery("The original target is unavailable."); return; }
            var epoch = Interlocked.Read(ref _insertionOperationEpoch);
            bool Current() => CanEdit && _isEditMode && SelectedPageId == destination.PageId &&
                Interlocked.Read(ref _insertionOperationEpoch) == epoch &&
                (destination.Placement != BlockInsertionPlacement.Replace || !HasDatabaseDrafts);
            await PublishRelativeDatabaseAsync(adapter, destination, ticket, Current);
        }
        catch (Exception error) { DatabaseLinkRecovery(error.Message.Split('\n')[0]); }
        finally { _creatingDatabase = false; }
    }

    private async Task PublishRelativeDatabaseAsync(ContractSessionAdapter adapter, DatabaseDestination destination,
        BoardInsertionTarget ticket, Func<bool> current)
    {
        if (_pendingDatabase is not { } reference || _pendingDatabaseBlock is not null) return;
        var prepared = new RichBoardBlock { Kind = "database", Database = reference, Width = 640, Height = 360 };
        var result = await adapter.InsertRelativeAsync(ticket, prepared, destination.Placement, current);
        if (!result.Applied)
        { DatabaseLinkRecovery(result.Message ?? "The insertion target changed or is unavailable."); return; }
        _pendingDatabaseBlock = result.SelectedBlockId!;
        if (current()) { _selectedBlockId = _pendingDatabaseBlock; RefreshAfterEdit(); }
        await SavePendingDatabaseLinkAsync();
    }

    private async Task SavePendingDatabaseLinkAsync()
    {
        var page = _session.Document.Sections.SelectMany(section => section.Pages)
            .FirstOrDefault(page => page.Id == _pendingDatabasePage);
        if (_pendingDatabase is null || _pendingDatabaseBlock is null ||
            page?.Blocks.FirstOrDefault(block => block.Id == _pendingDatabaseBlock)?.Database is not { } current ||
            current.StoreID != _pendingDatabase.StoreID || current.WorkbookID != _pendingDatabase.WorkbookID)
        { DatabaseLinkRecovery("The inserted Board reference changed; its link has not been confirmed."); return; }
        // Selecting another named view changes the link metadata, not the created artifact.
        // Save the current valid reference; never recreate or duplicate the inserted block.
        await _session.SaveAsync();
        ClearPendingDatabaseLink();
        Set("StatusText", "Database linked and saved.");
    }

    private void ClearPendingDatabaseLink()
    {
        _pendingDatabase = null; _pendingDatabasePage = null; _pendingDatabaseAnchor = null;
        _pendingDatabaseBlock = null; _pendingDatabaseDestination = null;
    }

    private void DatabaseLinkRecovery(string detail) => Set("StatusText", _pendingDatabase is null
        ? "Database creation failed: " + detail
        : "The database is retained in Data. " + detail + " Choose Link created database to finish its Board link.");
}
