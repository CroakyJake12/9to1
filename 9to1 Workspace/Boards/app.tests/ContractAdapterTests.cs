// Integration tests: ContractSessionAdapter merge between the CUI working
// model and the durable contract session. Proves the executable app path
// persists through physical .9to1board files without dropping data.

using CakeOS.Apps.Boards.Contract;
using System.Text.Json;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class ContractAdapterTests
{
    [Fact]
    public async Task Home_board_creation_intent_sends_a_validated_draft_without_mutating_the_pinned_board()
    {
        var original = CreateNotesDocument("Current pinned Board");
        var adapter = await ContractSessionAdapter.OpenExternallyWritableAsync(original,
            (_, _) => ValueTask.FromResult(true));
        await using (adapter)
        {
            var originalSnapshot = adapter.Snapshot;
            var host = new DecliningBoardCreationHost();
            var viewModel = new BoardsViewModel(adapter, homeBoardCreationHost: host);
            Assert.False(viewModel.CanSwitchBoard);
            Assert.True(viewModel.CanCreateBoard);

            await viewModel.DispatchAsync("NewBoard", null);

            Assert.NotNull(host.Draft);
            var draft = HavenBoardDocumentCodec.Deserialize(host.Draft!.Value);
            Assert.NotEqual(original.DocumentId, draft.DocumentId);
            Assert.Equal("Untitled board", draft.RichNotes!.Title);
            Assert.Equal(originalSnapshot, adapter.Snapshot);
            Assert.False(adapter.HasUnsavedChanges);
            Assert.Equal("New Board creation was canceled.", viewModel.Get("StatusText"));
        }
    }

    [Fact]
    public async Task Failed_post_commit_open_exposes_retry_for_the_same_created_board()
    {
        var adapter = await ContractSessionAdapter.OpenExternallyWritableAsync(CreateNotesDocument("Pinned Board"),
            (_, _) => ValueTask.FromResult(true));
        await using (adapter)
        {
            var host = new RetainedFailedOpenBoardCreationHost();
            var viewModel = new BoardsViewModel(adapter, homeBoardCreationHost: host);
            await viewModel.DispatchAsync("NewBoard", null);

            Assert.Equal(1, host.CreateCount);
            Assert.True(host.CanRetryOpenCreatedBoard);
            Assert.Equal("Board created, opening failed. Retry opens the same Board.", viewModel.Get("StatusText"));
            Assert.True(viewModel.TryGetValue("CanRetryHomeBoardOpen", out var available) && available is true);
            Assert.False(viewModel.TryGetValue("CanCreateBoard", out var canCreate) && canCreate is true);

            await viewModel.DispatchAsync("RetryOpenCreatedBoard", null);

            Assert.Equal(1, host.CreateCount);
            Assert.Equal(1, host.RetryCount);
            Assert.False(host.CanRetryOpenCreatedBoard);
            Assert.True(viewModel.TryGetValue("CanRetryHomeBoardOpen", out available) && available is false);
            Assert.Equal("Pinned Board", adapter.Document.Title);
        }
    }

    [Fact]
    public async Task Pending_home_settlement_blocks_duplicate_create_and_retries_only_the_original_attempt()
    {
        var adapter = await ContractSessionAdapter.OpenExternallyWritableAsync(CreateNotesDocument("Pinned Board"),
            (_, _) => ValueTask.FromResult(true));
        await using (adapter)
        {
            var host = new PendingSettlementBoardCreationHost();
            var viewModel = new BoardsViewModel(adapter, homeBoardCreationHost: host);
            await viewModel.DispatchAsync("NewBoard", null);
            Assert.Equal(1, host.CreateCount);
            Assert.False(viewModel.TryGetValue("CanCreateBoard", out var canCreate) && canCreate is true);
            Assert.True(viewModel.TryGetValue("CanRetryBoardCreationSettlement", out var canRetry) && canRetry is true);

            await viewModel.DispatchAsync("RetryBoardCreationSettlement", null);
            Assert.Equal(1, host.CreateCount);
            Assert.Equal(1, host.SettlementRetryCount);
            Assert.True(host.CanRetryOpenCreatedBoard);
            Assert.True(viewModel.TryGetValue("CanRetryHomeBoardOpen", out var canOpen) && canOpen is true);
            Assert.Equal("Board created, opening failed. Retry opens the same Board.", viewModel.Get("StatusText"));
        }
    }

    [Fact]
    public async Task Unknown_home_creation_outcome_blocks_duplicate_create_without_claiming_a_receipt()
    {
        var adapter = await ContractSessionAdapter.OpenExternallyWritableAsync(CreateNotesDocument("Pinned Board"),
            (_, _) => ValueTask.FromResult(true));
        await using (adapter)
        {
            var host = new UnknownBoardCreationHost();
            var viewModel = new BoardsViewModel(adapter, homeBoardCreationHost: host);
            await viewModel.DispatchAsync("NewBoard", null);
            await viewModel.DispatchAsync("NewBoard", null);

            Assert.Equal(1, host.CreateCount);
            Assert.False(viewModel.TryGetValue("CanCreateBoard", out var canCreate) && canCreate is true);
            Assert.False(viewModel.TryGetValue("CanRetryHomeBoardOpen", out var canOpen) && canOpen is true);
            Assert.False(viewModel.TryGetValue("CanRetryBoardCreationSettlement", out var canSettle) && canSettle is true);
            Assert.Equal("Unable to confirm whether this Board was saved. Creating another is disabled while Home checks.",
                viewModel.Get("StatusText"));
            Assert.True(viewModel.TryGetValue("CanCheckSavedBoard", out var canCheck) && canCheck is true);
            await viewModel.DispatchAsync("CheckSavedBoard", null);
            Assert.Equal(1, host.InspectCount);
            Assert.True(viewModel.TryGetValue("CanOpenRecordedBoard", out var canOpenRecorded) && canOpenRecorded is true);
            await viewModel.DispatchAsync("OpenRecordedBoard", null);
            Assert.Equal(1, host.OpenRecordedCount);
            Assert.Equal(1, host.CreateCount);
            Assert.Equal("Board opened; Home could not confirm the final save status. Creating another Board remains disabled.",
                viewModel.Get("StatusText"));
            Assert.False(viewModel.TryGetValue("CanCreateBoard", out canCreate) && canCreate is true);
            Assert.Equal("Pinned Board", adapter.Document.Title);
        }
    }

    [Fact]
    public async Task External_writable_session_requires_explicit_save_and_clears_only_after_acknowledgement()
    {
        var document = CreateNotesDocument("Home source");
        ReadOnlyMemory<byte> submitted = default;
        var callbackCount = 0;
        var adapter = await ContractSessionAdapter.OpenExternallyWritableAsync(document,
            (bytes, _) =>
            {
                Interlocked.Increment(ref callbackCount);
                submitted = bytes.ToArray();
                return ValueTask.FromResult(true);
            });
        await using (adapter)
        {
            Assert.True(adapter.CanEdit);
            Assert.False(adapter.CanSwitchBoard);
            Assert.False(adapter.CanSaveAs);
            var originalPageCount = adapter.Document.Sections[0].Pages.Count;
            await adapter.AddPageAsync(adapter.Document.Sections[0].Id);
            Assert.Equal(originalPageCount + 1, adapter.Document.Sections[0].Pages.Count);
            Assert.True(adapter.HasUnsavedChanges);
            await Task.Delay(900);
            Assert.Equal(0, Volatile.Read(ref callbackCount));

            await adapter.SaveAsync();

            Assert.Equal(1, Volatile.Read(ref callbackCount));
            Assert.False(adapter.HasUnsavedChanges);
            var saved = HavenBoardDocumentCodec.Deserialize(submitted);
            Assert.Equal(originalPageCount + 1, saved.RichNotes!.Sections[0].Pages.Count);
        }
    }

    [Fact]
    public async Task Rejected_external_save_keeps_draft_and_undo_history()
    {
        var adapter = await ContractSessionAdapter.OpenExternallyWritableAsync(CreateNotesDocument("Home source"),
            (_, _) => ValueTask.FromResult(false));
        await using (adapter)
        {
            var originalPageCount = adapter.Document.Sections[0].Pages.Count;
            await adapter.AddPageAsync(adapter.Document.Sections[0].Id);
            Assert.True(adapter.CanUndo);

            await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.SaveAsync().AsTask());

            Assert.True(adapter.HasUnsavedChanges);
            Assert.True(adapter.CanUndo);
            Assert.Equal(originalPageCount + 1, adapter.Document.Sections[0].Pages.Count);
            Assert.True(await adapter.UndoAsync());
            Assert.Equal(originalPageCount, adapter.Document.Sections[0].Pages.Count);
        }
    }

    [Fact]
    public async Task Table_reorder_command_moves_stable_cells_and_is_one_undoable_change()
    {
        var document = CreateNotesDocument("Table reorder");
        var notes = document.RichNotes!;
        var page = notes.Sections[0].Pages[0];
        var block = HavenRichNotesOps.AddBlock(notes, page.Id, HavenRichBlockKind.Table);
        var table = block.Table!;
        table.Rows[0].Cells[0].Text = "top-left";
        table.Rows[0].Cells[1].Text = "top-right";
        table.Rows[1].Cells[0].Text = "bottom-left";
        table.Rows[1].Cells[1].Text = "bottom-right";
        table.Rows[1].Cells[0].Bold = true;
        table.ColumnWidths = [120, 240];
        table.RowHeights = [30, 60];
        var movedRowCellIds = table.Rows[1].Cells.Select(cell => cell.Id).ToArray();
        var movedColumnCellIds = table.Rows.Select(row => row.Cells[1].Id).ToArray();
        var adapter = await ContractSessionAdapter.OpenExternallyWritableAsync(document,
            (_, _) => ValueTask.FromResult(true));
        await using (adapter)
        {
            var viewModel = new BoardsViewModel(adapter);
            viewModel.FocusBlock(block.Id);
            viewModel.FocusCell(block.Id, 1, 0);
            await viewModel.DispatchAsync("ContextMoveRowUp", null);

            var afterRowMove = await adapter.GetDetachedContractDocumentAsync();
            var movedTable = afterRowMove.RichNotes!.Sections[0].Pages[0].Blocks.Single(candidate => candidate.Id == block.Id).Table!;
            Assert.Equal(movedRowCellIds, movedTable.Rows[0].Cells.Select(cell => cell.Id));
            Assert.Equal("bottom-left", movedTable.Rows[0].Cells[0].Text);
            Assert.True(movedTable.Rows[0].Cells[0].Bold);
            Assert.Equal(new[] { 60d, 30d, 0d }, movedTable.RowHeights);
            Assert.Equal((block.Id, 0, 0), viewModel.SelectedCell);
            Assert.True(await adapter.UndoAsync());
            var restoredRows = await adapter.GetDetachedContractDocumentAsync();
            var restoredTable = restoredRows.RichNotes!.Sections[0].Pages[0].Blocks.Single(candidate => candidate.Id == block.Id).Table!;
            Assert.Equal(new[] { 30d, 60d }, restoredTable.RowHeights);
            Assert.Equal("top-left", restoredTable.Rows[0].Cells[0].Text);

            viewModel.FocusCell(block.Id, 0, 0);
            await viewModel.DispatchAsync("ContextMoveColumnRight", null);
            var afterColumnMove = await adapter.GetDetachedContractDocumentAsync();
            var movedColumns = afterColumnMove.RichNotes!.Sections[0].Pages[0].Blocks.Single(candidate => candidate.Id == block.Id).Table!;
            Assert.Equal(movedColumnCellIds, movedColumns.Rows.Select(row => row.Cells[0].Id));
            Assert.Equal(new[] { 240d, 120d, 0d }, movedColumns.ColumnWidths);
            Assert.Equal("top-right", movedColumns.Rows[0].Cells[0].Text);
            Assert.True(await adapter.UndoAsync());
            var restoredColumns = await adapter.GetDetachedContractDocumentAsync();
            var originalColumns = restoredColumns.RichNotes!.Sections[0].Pages[0].Blocks.Single(candidate => candidate.Id == block.Id).Table!;
            Assert.Equal(new[] { 120d, 240d }, originalColumns.ColumnWidths);
            Assert.Equal("top-left", originalColumns.Rows[0].Cells[0].Text);
        }
    }

    [Fact]
    public async Task Exact_table_dimensions_are_one_undoable_edit_and_reopen_with_merged_cell_identity()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "table-resize.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            var pageId = string.Empty;
            var blockId = string.Empty;
            var cellIds = Array.Empty<string>();
            await using (var seed = await RichBoardSession.CreateNewAsync(store, "Table resize"))
            {
                await seed.SaveAsAsync(path);
                pageId = seed.Rich.Sections[0].Pages[0].Id;
                await seed.MutateAsync(rich =>
                {
                    var block = HavenRichNotesOps.AddBlock(rich, pageId, HavenRichBlockKind.Table);
                    blockId = block.Id;
                    block.Table!.Rows[0].Cells[0].Text = "Anchor";
                    block.Table.Rows[0].Cells[1].Text = "Covered content";
                    Assert.True(HavenRichNotesOps.MergeTableCellRight(rich, pageId, block.Id, 0, 0));
                    cellIds = block.Table.Rows.SelectMany(row => row.Cells).Select(cell => cell.Id).ToArray();
                });
                await seed.SaveAsync();
            }

            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var viewModel = new BoardsViewModel(adapter);
                viewModel.FocusBlock(blockId);
                viewModel.FocusCell(blockId, 1, 1);
                Assert.True(viewModel.TrySetValue("ContextColumnWidthDraft", 312d));
                Assert.True(viewModel.TrySetValue("ContextRowHeightDraft", 144d));
                await viewModel.DispatchAsync("ContextApplyCellDimensions", null);

                var resizedDocument = await adapter.GetDetachedContractDocumentAsync();
                var resizedTable = resizedDocument.RichNotes!.Sections[0].Pages[0].Blocks
                    .Single(block => block.Id == blockId).Table!;
                Assert.Equal(312, resizedTable.ColumnWidths[1]);
                Assert.Equal(144, resizedTable.RowHeights[1]);
                Assert.Equal(cellIds, resizedTable.Rows.SelectMany(row => row.Cells).Select(cell => cell.Id));
                Assert.True(adapter.CanUndo);
                Assert.True(await adapter.UndoAsync());
                var undoneDocument = await adapter.GetDetachedContractDocumentAsync();
                var undoneTable = undoneDocument.RichNotes!.Sections[0].Pages[0].Blocks
                    .Single(block => block.Id == blockId).Table!;
                Assert.Empty(undoneTable.ColumnWidths);
                Assert.Empty(undoneTable.RowHeights);
                Assert.Equal(cellIds, undoneTable.Rows.SelectMany(row => row.Cells).Select(cell => cell.Id));
                Assert.True(await adapter.RedoAsync());
                await adapter.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            var document = await reopened.GetDetachedContractDocumentAsync();
            var table = document.RichNotes!.Sections[0].Pages[0].Blocks.Single(block => block.Id == blockId).Table!;
            Assert.Equal(312, table.ColumnWidths[1]);
            Assert.Equal(144, table.RowHeights[1]);
            Assert.Equal(cellIds, table.Rows.SelectMany(row => row.Cells).Select(cell => cell.Id));
            Assert.Equal(cellIds[1], table.Rows[0].Cells[1].Id);
            Assert.Equal(table.Rows[0].Cells[0].Id, table.Rows[0].Cells[1].CoveredByCellId);
            Assert.Equal("Covered content", table.Rows[0].Cells[1].Text);
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task Table_merge_and_split_context_actions_preserve_stable_blank_slots_and_undo()
    {
        var document = CreateNotesDocument("Table merge actions");
        var notes = document.RichNotes!;
        var page = notes.Sections[0].Pages[0];
        var block = HavenRichNotesOps.AddBlock(notes, page.Id, HavenRichBlockKind.Table);
        var table = block.Table!;
        table.Rows[0].Cells[0].Text = "Heading";
        table.Rows[0].Cells[1].Background = "#FFE8F0FE";
        var anchorId = table.Rows[0].Cells[0].Id;
        var coveredId = table.Rows[0].Cells[1].Id;
        var adapter = await ContractSessionAdapter.OpenExternallyWritableAsync(document,
            (_, _) => ValueTask.FromResult(true));
        await using (adapter)
        {
            var viewModel = new BoardsViewModel(adapter);
            viewModel.FocusBlock(block.Id);
            viewModel.FocusCell(block.Id, 0, 0);
            Assert.True(viewModel.TryGetValue("ContextCanMergeCellRight", out var canMerge) && canMerge is true);
            await viewModel.DispatchAsync("ContextMergeCellRight", null);
            Assert.Equal("Table cells merged; covered values and formatting remain recoverable on split.", viewModel.Get("StatusText"));
            var mergedDocument = await adapter.GetDetachedContractDocumentAsync();
            var merged = mergedDocument.RichNotes!.Sections[0].Pages[0].Blocks.Single(candidate => candidate.Id == block.Id).Table!;
            Assert.Equal(2, merged.Rows[0].Cells[0].ColumnSpan);
            Assert.Equal(anchorId, merged.Rows[0].Cells[1].CoveredByCellId);
            Assert.Equal("Heading", merged.Rows[0].Cells[0].Text);
            Assert.True(viewModel.TryGetValue("ContextCanSplitCell", out var canSplit) && canSplit is true);

            await viewModel.DispatchAsync("ContextSplitCell", null);
            var splitDocument = await adapter.GetDetachedContractDocumentAsync();
            var split = splitDocument.RichNotes!.Sections[0].Pages[0].Blocks.Single(candidate => candidate.Id == block.Id).Table!;
            Assert.Equal(1, split.Rows[0].Cells[0].ColumnSpan);
            Assert.Null(split.Rows[0].Cells[1].CoveredByCellId);
            Assert.Equal(coveredId, split.Rows[0].Cells[1].Id);
            Assert.Equal("#FFE8F0FE", split.Rows[0].Cells[1].Background);
            Assert.True(viewModel.TryGetValue("ContextCanChangeTableStructure", out var canChangeStructure) && canChangeStructure is true);
            Assert.True(viewModel.TrySetValue("ContextHasHeaderRow", true));
            Assert.True(viewModel.TrySetValue("ContextHasHeaderColumn", true));
            Assert.True(viewModel.TrySetValue("ContextTableAlignmentIndex", 2));
            await viewModel.DispatchAsync("ContextTableWider", null);
            var formattedDocument = await adapter.GetDetachedContractDocumentAsync();
            var formatted = formattedDocument.RichNotes!.Sections[0].Pages[0].Blocks.Single(candidate => candidate.Id == block.Id).Table!;
            Assert.True(formatted.HasHeaderRow);
            Assert.True(formatted.HasHeaderColumn);
            Assert.Equal(HavenRichAlignment.Center, formatted.Alignment);
            Assert.Equal(680, formatted.Width);
            for (var undo = 0; undo < 5; undo++) Assert.True(await adapter.UndoAsync());
            var undone = await adapter.GetDetachedContractDocumentAsync();
            var undoneTable = undone.RichNotes!.Sections[0].Pages[0].Blocks.Single(candidate => candidate.Id == block.Id).Table!;
            Assert.Equal(2, undoneTable.Rows[0].Cells[0].ColumnSpan);
        }
    }

    private static HavenBoardDocument CreateNotesDocument(string title)
    {
        var now = DateTimeOffset.UtcNow;
        return new HavenBoardDocument(HavenBoardDocument.FormatIdentity,
            HavenBoardDocument.CurrentSchemaVersion, Guid.NewGuid(), now, now,
            Snapshot: null, RichNotes: HavenRichNotes.Create(title));
    }

    private sealed class DecliningBoardCreationHost : IBoardsHomeBoardCreationHost
    {
        public ReadOnlyMemory<byte>? Draft { get; private set; }
        public bool CanRetryOpenCreatedBoard => false;
        public bool CanRetryCreationSettlement => false;
        public bool CanInspectUnknownCreation => false;
        public bool CanOpenRecordedBoard => false;
        public bool HasUnresolvedCreation => false;

        public Task<BoardsHomeBoardCreationResult> CreateAndOpenAsync(ReadOnlyMemory<byte> validatedDraft,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Draft = validatedDraft.ToArray();
            return Task.FromResult(new BoardsHomeBoardCreationResult(
                BoardsHomeBoardCreationOutcome.CancelledBeforeCreate));
        }

        public Task<BoardsHomeBoardCreationResult> RetryOpenCreatedBoardAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(
                BoardsHomeBoardCreationOutcome.CreatedOpenFailed, "NoPendingOpen"));

        public Task<BoardsHomeBoardCreationResult> RetryCreationSettlementAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(
                BoardsHomeBoardCreationOutcome.OutcomeUnknown, "NoPendingSettlement"));
        public Task<BoardsHomeBoardCreationResult> InspectUnknownCreationAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown, "NoUnknownAttempt"));
        public Task<BoardsHomeBoardCreationResult> OpenRecordedBoardAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown, "NoRecordedReceipt"));
    }

    private sealed class RetainedFailedOpenBoardCreationHost : IBoardsHomeBoardCreationHost
    {
        private Guid _createdDocumentId;
        public int CreateCount { get; private set; }
        public int RetryCount { get; private set; }
        public bool CanRetryOpenCreatedBoard { get; private set; }
        public bool CanRetryCreationSettlement => false;
        public bool CanInspectUnknownCreation => false;
        public bool CanOpenRecordedBoard => false;
        public bool HasUnresolvedCreation => CanRetryOpenCreatedBoard;

        public Task<BoardsHomeBoardCreationResult> CreateAndOpenAsync(ReadOnlyMemory<byte> validatedDraft,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateCount++;
            _createdDocumentId = HavenBoardDocumentCodec.Deserialize(validatedDraft).DocumentId;
            CanRetryOpenCreatedBoard = true;
            return Task.FromResult(new BoardsHomeBoardCreationResult(
                BoardsHomeBoardCreationOutcome.CreatedOpenFailed, "RouteUnavailable"));
        }

        public Task<BoardsHomeBoardCreationResult> RetryOpenCreatedBoardAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.NotEqual(Guid.Empty, _createdDocumentId);
            RetryCount++;
            CanRetryOpenCreatedBoard = false;
            return Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.Opened));
        }

        public Task<BoardsHomeBoardCreationResult> RetryCreationSettlementAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(
                BoardsHomeBoardCreationOutcome.OutcomeUnknown, "NoPendingSettlement"));
        public Task<BoardsHomeBoardCreationResult> InspectUnknownCreationAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown, "NoUnknownAttempt"));
        public Task<BoardsHomeBoardCreationResult> OpenRecordedBoardAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown, "NoRecordedReceipt"));
    }

    private sealed class PendingSettlementBoardCreationHost : IBoardsHomeBoardCreationHost
    {
        public int CreateCount { get; private set; }
        public int SettlementRetryCount { get; private set; }
        public bool CanRetryOpenCreatedBoard { get; private set; }
        public bool CanRetryCreationSettlement { get; private set; }
        public bool CanInspectUnknownCreation => false;
        public bool CanOpenRecordedBoard => false;
        public bool HasUnresolvedCreation => CanRetryOpenCreatedBoard || CanRetryCreationSettlement;

        public Task<BoardsHomeBoardCreationResult> CreateAndOpenAsync(ReadOnlyMemory<byte> validatedDraft,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = HavenBoardDocumentCodec.Deserialize(validatedDraft);
            CreateCount++;
            CanRetryCreationSettlement = true;
            return Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.HomeAuditPending));
        }

        public Task<BoardsHomeBoardCreationResult> RetryCreationSettlementAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SettlementRetryCount++;
            CanRetryCreationSettlement = false;
            CanRetryOpenCreatedBoard = true;
            return Task.FromResult(new BoardsHomeBoardCreationResult(
                BoardsHomeBoardCreationOutcome.CreatedOpenFailed, "RouteUnavailable"));
        }

        public Task<BoardsHomeBoardCreationResult> RetryOpenCreatedBoardAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown));
        public Task<BoardsHomeBoardCreationResult> InspectUnknownCreationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown));
        public Task<BoardsHomeBoardCreationResult> OpenRecordedBoardAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown));
    }

    private sealed class UnknownBoardCreationHost : IBoardsHomeBoardCreationHost
    {
        public int CreateCount { get; private set; }
        public int InspectCount { get; private set; }
        public int OpenRecordedCount { get; private set; }
        public bool CanRetryOpenCreatedBoard => false;
        public bool CanRetryCreationSettlement => false;
        private bool _unresolved = true;
        private bool _canOpenRecorded;
        public bool CanInspectUnknownCreation => _unresolved && !_canOpenRecorded;
        public bool CanOpenRecordedBoard => _canOpenRecorded;
        public bool HasUnresolvedCreation { get; private set; }

        public Task<BoardsHomeBoardCreationResult> CreateAndOpenAsync(ReadOnlyMemory<byte> validatedDraft,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = HavenBoardDocumentCodec.Deserialize(validatedDraft);
            CreateCount++;
            HasUnresolvedCreation = true;
            _unresolved = true;
            return Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown));
        }

        public Task<BoardsHomeBoardCreationResult> RetryCreationSettlementAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown));

        public Task<BoardsHomeBoardCreationResult> RetryOpenCreatedBoardAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown));

        public Task<BoardsHomeBoardCreationResult> InspectUnknownCreationAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectCount++;
            _unresolved = false;
            _canOpenRecorded = true;
            return Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.CommittedWithUncertainAudit));
        }

        public Task<BoardsHomeBoardCreationResult> OpenRecordedBoardAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenRecordedCount++;
            _canOpenRecorded = false;
            return Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OpenedWithAuditWarning));
        }
    }

    [Fact]
    public async Task Verified_document_bytes_open_through_a_detached_read_only_session()
    {
        var now = DateTimeOffset.UtcNow;
        var document = new HavenBoardDocument(HavenBoardDocument.FormatIdentity,
            HavenBoardDocument.CurrentSchemaVersion, Guid.NewGuid(), now, now,
            Snapshot: null, RichNotes: HavenRichNotes.Create("Read-only source"));
        var imageBlock = new HavenRichBlock
        {
            Kind = HavenRichBlockKind.Image,
            Image = new HavenRichImage { DisplayName = "remote.png", LocalReference = "sidecar:" + new string('a', 64) }
        };
        document.RichNotes!.Sections[0].Pages[0].Blocks.Add(imageBlock);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var originalBytes = bytes.ToArray();
        var decoded = HavenBoardDocumentCodec.Deserialize(bytes);

        await using var adapter = await ContractSessionAdapter.OpenReadOnlyAsync(decoded);
        var vm = new BoardsViewModel(adapter);
        Assert.False(adapter.CanEdit);
        Assert.False(vm.IsEditMode);
        Assert.False(vm.CanToggleEditMode);
        Assert.False(adapter.CanUndo);
        Assert.False(adapter.CanRedo);
        Assert.Null(await adapter.GetImageBytesAsync(imageBlock.Id));

        var originalPageCount = adapter.Document.Sections[0].Pages.Count;
        await vm.DispatchAsync("AddPage", null);
        await vm.DispatchAsync("ToggleEditView", null);
        await vm.DispatchAsync("SaveBoard", null);

        Assert.Equal(originalPageCount, adapter.Document.Sections[0].Pages.Count);
        adapter.Document.Title = "Detached presentation copy";
        Assert.Equal("Read-only source", decoded.RichNotes!.Title);
        Assert.True(originalBytes.SequenceEqual(bytes));
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.SaveAsync().AsTask());
    }

    [Fact]
    public void Bounded_document_codec_rejects_future_and_over_limit_payloads()
    {
        var future = "{\"format\":\"9to1.board\",\"schemaVersion\":999}"u8.ToArray();
        var unsupported = Assert.Throws<UnsupportedHavenBoardDocumentVersionException>(() =>
            HavenBoardDocumentCodec.Deserialize(future));
        Assert.Equal(999, unsupported.ActualVersion);

        var oversized = new byte[HavenBoardDocumentCodec.MaximumDocumentBytes + 1];
        Assert.Throws<InvalidDataException>(() => HavenBoardDocumentCodec.Deserialize(oversized));
    }

    [Fact]
    public async Task Image_formatting_metadata_survives_save_and_reopen()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "image-formatting.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            string pageId;
            string blockId;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var page = adapter.Document.Sections[0].Pages[0];
                pageId = page.Id;
                var image = new RichBoardBlock
                {
                    Kind = "image",
                    Image = new RichImageView { DisplayName = "diagram.png", DataBase64 = "AQ==" }
                };
                blockId = image.Id;
                page.Blocks.Add(image);
                adapter.MarkDirty();
                await adapter.SaveAsync();

                var liveImage = adapter.Document.Sections.SelectMany(section => section.Pages)
                    .Single(candidatePage => candidatePage.Id == pageId)
                    .Blocks.Single(block => block.Id == blockId).Image!;
                liveImage.Width = 360;
                liveImage.Height = 240;
                liveImage.KeepAspectRatio = false;
                liveImage.RotationDegrees = 90;
                liveImage.CropLeft = 0.1;
                liveImage.CropRight = 0.05;
                liveImage.CropTop = 0.2;
                liveImage.FitMode = "cover";
                liveImage.Alignment = "center";
                liveImage.Caption = "Rendered diagram";
                liveImage.AltText = "A labelled diagram";
                liveImage.LinkUri = "https://example.test/reference";
                adapter.MarkDirty();
                await adapter.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            var saved = Assert.Single(reopened.Document.Sections[0].Pages.Single(page => page.Id == pageId).Blocks,
                block => block.Id == blockId);
            var projected = saved.Image!;
            Assert.Equal(360, projected.Width);
            Assert.Equal(240, projected.Height);
            Assert.False(projected.KeepAspectRatio);
            Assert.Equal(90, projected.RotationDegrees);
            Assert.Equal(0.1, projected.CropLeft);
            Assert.Equal(0.05, projected.CropRight);
            Assert.Equal(0.2, projected.CropTop);
            Assert.Equal("cover", projected.FitMode);
            Assert.Equal("center", projected.Alignment);
            Assert.Equal("Rendered diagram", projected.Caption);
            Assert.Equal("A labelled diagram", projected.AltText);
            Assert.Equal("https://example.test/reference", projected.LinkUri);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Adapter_edit_save_dispose_reopen_preserves_notes_ink_and_table()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "adapter.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            string pageId;

            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                adapter.Document.Title = "Adapter board";
                var page = adapter.Document.Sections[0].Pages[0];
                pageId = page.Id;
                var para = page.Blocks.First(b => b.Kind == "paragraph");
                para.Text = "Adapter line one\nAdapter line two";
                para.Bold = true;
                page.Blocks.Add(new RichBoardBlock { Kind = "checklist", Text = "Adapter checklist", IsChecked = true });
                adapter.MarkDirty();
                await adapter.CommitInkStrokeAsync(pageId, [(1, 1), (2, 2)]);
                await adapter.SaveAsync();
                Assert.StartsWith("Saved", adapter.Status, StringComparison.Ordinal);
            }

            // Add new ink to a board that already contains persisted ink.
            await using (var resumed = await ContractSessionAdapter.OpenAsync(store, path))
            {
                await resumed.CommitInkStrokeAsync(pageId, [(5, 5), (50, 25), (90, 10)]);
                await resumed.SaveAsync();
            }

            await using (var reopened = await ContractSessionAdapter.OpenAsync(store, path))
            {
                Assert.Equal("Adapter board", reopened.Document.Title);
                var para = reopened.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "paragraph");
                Assert.Equal("Adapter line one\nAdapter line two", para.Text);
                Assert.True(para.Bold);
                var check = reopened.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "checklist");
                Assert.Equal("Adapter checklist", check.Text);
                Assert.True(check.IsChecked);
                var ink = await reopened.GetInkStrokesAsync(reopened.Document.Sections[0].Pages[0].Id);
                Assert.Equal(2, ink.Count);
                Assert.Equal(2, ink[0].Points.Count);
                Assert.Equal(3, ink[1].Points.Count);
            }

            // Raw contract read proves the bytes hold real ink points, not just a counter.
            var raw = await store.LoadDocumentAtPathAsync(path);
            Assert.NotNull(raw?.RichNotes);
            Assert.Equal(2, raw!.RichNotes!.Sections[0].Pages[0].Ink.Count);
            var stroke = raw.RichNotes.Sections[0].Pages[0].Ink[1];
            Assert.Equal(3, stroke.Points.Count);
            Assert.Equal(90, stroke.Points[2].X);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Canvas_lock_is_page_scoped_persistent_and_blocks_layout_edits_only()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "canvas-lock.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            string pageId;
            string objectId;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                pageId = adapter.Document.Sections[0].Pages[0].Id;
                var otherPageId = await adapter.AddPageAsync(adapter.Document.Sections[0].Id, "Other page");
                await adapter.AddCanvasObjectAsync(pageId, "Text", "Keep position", 48, 72);
                var box = Assert.Single(await adapter.GetCanvasObjectsAsync(pageId));
                objectId = box.Id;
                var vm = new BoardsViewModel(adapter);
                await vm.ToggleCanvasLockAsync();
                Assert.True(vm.IsCanvasLocked);
                Assert.False(adapter.Document.Sections[0].Pages.Single(item => item.Id == otherPageId).IsCanvasLocked);
                await vm.MoveCanvasBoxAsync(objectId, 240, 180);
                box = Assert.Single(await adapter.GetCanvasObjectsAsync(pageId));
                Assert.Equal((48d, 72d), (box.X, box.Y));
                await vm.ToggleCanvasLockAsync();
                Assert.False(vm.IsCanvasLocked);
                await vm.MoveCanvasBoxAsync(objectId, 240, 180);
                await vm.ToggleCanvasLockAsync();
                await vm.DispatchAsync("ToggleEditView", null);
                await vm.MoveCanvasBoxAsync(objectId, 300, 300);
                box = Assert.Single(await adapter.GetCanvasObjectsAsync(pageId));
                Assert.Equal((240d, 180d), (box.X, box.Y));
                await adapter.CommitInkStrokeAsync(pageId, [(1, 2), (3, 4)]);
                await adapter.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            var page = reopened.Document.Sections[0].Pages.Single(item => item.Id == pageId);
            Assert.True(page.IsCanvasLocked);
            var savedBox = Assert.Single(await reopened.GetCanvasObjectsAsync(pageId));
            Assert.Equal(objectId, savedBox.Id);
            Assert.Equal((240d, 180d), (savedBox.X, savedBox.Y));
            Assert.Single(await reopened.GetInkStrokesAsync(pageId));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Paragraph_split_and_merge_preserve_runs_undo_and_reopen()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "text-split-merge.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            string pageId;
            string originalBlockId;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var page = adapter.Document.Sections[0].Pages[0];
                pageId = page.Id;
                var paragraph = page.Blocks.First(block => block.Kind == "paragraph");
                originalBlockId = paragraph.Id;
                paragraph.Text = "alphaBETAomega";
                paragraph.Runs =
                [
                    new HavenRichTextRun { Text = "alpha" },
                    new HavenRichTextRun { Text = "BETA", Bold = true, Foreground = "#FF345678" },
                    new HavenRichTextRun { Text = "omega" },
                ];
                adapter.MarkDirty();
                await adapter.SaveAsync();

                var vm = new BoardsViewModel(adapter);
                Assert.True(await vm.SplitTextBlockAtAsync(originalBlockId, 7));
                var blocks = adapter.Document.Sections[0].Pages[0].Blocks;
                Assert.Equal(new[] { "alphaBE", "TAomega" }, blocks.Select(block => block.Text));
                Assert.Equal(new[] { "alpha", "BE" }, blocks[0].Runs.Select(run => run.Text));
                Assert.Equal(new[] { "TA", "omega" }, blocks[1].Runs.Select(run => run.Text));
                Assert.True(blocks[0].Runs[1].Bold);
                Assert.True(blocks[1].Runs[0].Bold);

                Assert.True(await adapter.UndoAsync());
                blocks = adapter.Document.Sections[0].Pages[0].Blocks;
                Assert.Single(blocks, block => block.Id == originalBlockId);
                Assert.Equal("alphaBETAomega", blocks.Single(block => block.Id == originalBlockId).Text);
                Assert.True(await adapter.RedoAsync());

                var splitId = adapter.Document.Sections[0].Pages[0].Blocks[1].Id;
                Assert.True(await vm.MergeTextBlockWithPreviousAsync(splitId));
                blocks = adapter.Document.Sections[0].Pages[0].Blocks;
                var merged = Assert.Single(blocks, block => block.Id == originalBlockId);
                Assert.Equal("alphaBETAomega", merged.Text);
                Assert.Equal(new[] { "alpha", "BE", "TA", "omega" }, merged.Runs.Select(run => run.Text));
                Assert.True(merged.Runs[1].Bold && merged.Runs[2].Bold);
                Assert.Equal("#FF345678", merged.Runs[2].Foreground);

                Assert.True(await adapter.UndoAsync());
                Assert.Equal(2, adapter.Document.Sections[0].Pages[0].Blocks.Count(block =>
                    block.Kind is "paragraph" or "heading"));
                Assert.True(await adapter.RedoAsync());
                await adapter.SaveAsync();

                await vm.DispatchAsync("ToggleEditView", null);
                Assert.False(await vm.SplitTextBlockAtAsync(originalBlockId, 0));
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            var saved = Assert.Single(reopened.Document.Sections[0].Pages.Single(page => page.Id == pageId).Blocks,
                block => block.Id == originalBlockId);
            Assert.Equal("alphaBETAomega", saved.Text);
            Assert.Equal(new[] { "alpha", "BE", "TA", "omega" }, saved.Runs.Select(run => run.Text));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Adapter_merge_preserves_contract_only_state()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "preserve.9to1board");
            using var store = new JsonFileHavenBoardStore(root);

            // Seed contract-only richness the editor UI cannot express.
            await using (var seed = await RichBoardSession.CreateNewAsync(store, "Seeded"))
            {
                await seed.SaveAsAsync(path);
                var pageId = seed.Rich.Sections[0].Pages[0].Id;
                await seed.MutateAsync(rich =>
                {
                    var page = rich.Sections[0].Pages[0];
                    page.Blocks.Clear();
                    var styled = HavenRichBlock.Paragraph("ignored");
                    styled.Runs =
                    [
                        new HavenRichTextRun { Text = "plain-", Bold = false },
                        new HavenRichTextRun { Text = "bold", Bold = true }
                    ];
                    page.Blocks.Add(styled);
                    var table = new HavenRichBlock { Kind = HavenRichBlockKind.Table, Table = HavenRichTable.Create(3, 3) };
                    table.Table.Rows[2].Cells[2].Text = "extra";
                    page.Blocks.Add(table);
                    page.Canvas.Add(new HavenRichCanvasObject { Kind = "Text", Text = "canvas-keep", X = 10, Y = 10 });
                    var attachBlock = HavenRichBlock.Paragraph("with file");
                    attachBlock.Attachment = new HavenRichAttachmentRef
                    {
                        DisplayName = "keep.txt",
                        DataBase64 = Convert.ToBase64String("keep"u8.ToArray())
                    };
                    page.Blocks.Add(attachBlock);
                });
                await seed.SaveAsync();
            }

            // Edit only the title and plain text through the adapter working model.
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                adapter.Document.Title = "Retitled";
                var para = adapter.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "paragraph");
                para.Text = "plain-bold";
                adapter.MarkDirty();
                await adapter.SaveAsync();
            }

            var raw = await store.LoadDocumentAtPathAsync(path);
            Assert.NotNull(raw?.RichNotes);
            var page = raw!.RichNotes!.Sections[0].Pages[0];
            Assert.Equal("Retitled", raw.RichNotes.Title);
            var styled = page.Blocks.First(b => b.Kind == HavenRichBlockKind.Paragraph);
            Assert.Equal("plain-bold", styled.PlainText);
            Assert.Equal(2, styled.Runs.Count);
            Assert.True(styled.Runs[1].Bold);
            Assert.Equal("extra", page.Blocks
                .First(b => b.Kind == HavenRichBlockKind.Table).Table!.Rows[2].Cells[2].Text);
            Assert.Equal("canvas-keep", Assert.Single(page.Canvas).Text);
            Assert.Equal("keep.txt", page.Blocks
                .First(b => b.Attachment is not null).Attachment!.DisplayName);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Adapter_structural_adds_create_stable_contract_entities()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "structural.9to1board");
            using var store = new JsonFileHavenBoardStore(root);

            string sectionId, pageId;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var section = new RichBoardSection { Title = "Added section" };
                section.Pages.Add(new RichBoardPage { Title = "Added page" });
                adapter.Document.Sections.Add(section);
                sectionId = section.Id;
                pageId = section.Pages[0].Id;
                adapter.MarkDirty();
                await adapter.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.Contains(reopened.Document.Sections, s => s.Id == sectionId && s.Title == "Added section");
            var added = reopened.Document.Sections.First(s => s.Id == sectionId);
            Assert.Contains(added.Pages, p => p.Id == pageId && p.Title == "Added page");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Adapter_eraser_hit_test_removes_only_nearby_strokes()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "eraser.9to1board");
            using var store = new JsonFileHavenBoardStore(root);

            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var pageId = adapter.Document.Sections[0].Pages[0].Id;
                await adapter.CommitInkStrokeAsync(pageId, [(10, 10), (20, 20)]);
                await adapter.CommitInkStrokeAsync(pageId, [(400, 400), (410, 410)]);
                Assert.Equal(0, await adapter.EraseInkAtPageAsync(pageId, 200, 200, 5));
                Assert.Equal(1, await adapter.EraseInkAtPageAsync(pageId, 12, 12, 12));
                await adapter.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            var ink = await reopened.GetInkStrokesAsync(reopened.Document.Sections[0].Pages[0].Id);
            Assert.Single(ink);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InMemory_dispose_flushes_pending_edits_without_save_call()
    {
        InMemoryRichBoardSession.ClearStore();
        const string path = "memory://boards/dispose-flush-test";
        await using (var session = new InMemoryRichBoardSession())
        {
            await session.OpenAsync(path);
            session.Document.Title = "Flushed on close";
            session.MarkDirty();
            // No explicit SaveAsync: DisposeAsync must still persist.
        }

        await using var reopened = new InMemoryRichBoardSession();
        await reopened.OpenAsync(path);
        Assert.Equal("Flushed on close", reopened.Document.Title);
    }

    [Fact]
    public async Task Adapter_normalizes_extension_on_create_and_prefers_sibling_on_open()
    {
        var root = TempDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var bare = Path.Combine(root, "My Board");

            await using (var created = await ContractSessionAdapter.OpenAsync(store, bare))
            {
                Assert.Equal(bare + ".9to1board", created.FilePath);
                await created.SaveAsync();
            }
            Assert.True(File.Exists(bare + ".9to1board"));
            Assert.False(File.Exists(bare));

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, bare);
            Assert.Equal(bare + ".9to1board", reopened.FilePath);
            Assert.Equal("My Board", reopened.Document.Title);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Adapter_canvas_drag_move_persists_across_reopen()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "drag.9to1board");
            using var store = new JsonFileHavenBoardStore(root);

            string boxId;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var pageId = adapter.Document.Sections[0].Pages[0].Id;
                boxId = await adapter.AddCanvasObjectAsync(pageId, "Text", "Drag me", 10, 10);
                // One committed move per drag gesture, as the pointer handler does on release.
                Assert.True(await adapter.MoveCanvasObjectAsync(pageId, boxId, 300, 220));
                await adapter.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            var boxes = await reopened.GetCanvasObjectsAsync(reopened.Document.Sections[0].Pages[0].Id);
            var box = Assert.Single(boxes);
            Assert.Equal(boxId, box.Id);
            Assert.Equal(300, box.X);
            Assert.Equal(220, box.Y);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Adapter_ink_view_pan_zoom_persists_across_reopen()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "inkview.9to1board");
            using var store = new JsonFileHavenBoardStore(root);

            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var session = Assert.IsType<ContractSessionAdapter>(adapter);
                var pageId = session.Document.Sections[0].Pages[0].Id;
                await session.SetInkViewAsync(pageId, 120, -40, 2);
                await adapter.SaveAsync();
            }

            var raw = await store.LoadDocumentAtPathAsync(path);
            var view = raw!.RichNotes!.Sections[0].Pages[0].InkView;
            Assert.Equal(120, view.PanX);
            Assert.Equal(-40, view.PanY);
            Assert.Equal(2, view.Zoom);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Selected_page_ink_and_freeform_are_isolated_and_reopen_with_their_own_viewport()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "page-routing.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            string sectionId;
            string firstPageId;
            string secondPageId;

            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var section = adapter.Document.Sections[0];
                sectionId = section.Id;
                firstPageId = section.Pages[0].Id;
                var secondPage = new RichBoardPage { Title = "Second page" };
                section.Pages.Add(secondPage);
                secondPageId = secondPage.Id;
                adapter.MarkDirty();
                await adapter.SaveAsync();

                var viewModel = new BoardsViewModel(adapter);
                await viewModel.CommitInkStrokeAsync(firstPageId, [(10, 10, 0.6), (20, 20, 0.8)], 2, "#FF111111", "Pen");
                viewModel.SelectPage(sectionId, secondPageId);
                Assert.Equal(1, viewModel.InkZoom);
                await viewModel.CommitInkStrokeAsync(secondPageId, [(210, 210, 0.7), (220, 220, 0.9)], 3, "#FF1122AA", "Pen");
                await viewModel.PanInkViewAsync(15, -7);
                await viewModel.SetInkZoomAsync(2);

                viewModel.SelectPage(sectionId, firstPageId);
                Assert.Equal(1, viewModel.InkZoom);
                viewModel.SelectPage(sectionId, secondPageId);
                Assert.Equal(2, viewModel.InkZoom);

                Assert.False(await adapter.SelectInkAtPageAsync(secondPageId, 10, 10));
                Assert.Equal(0, await adapter.EraseInkAtPageAsync(secondPageId, 10, 10, 6));
                Assert.True(await adapter.SelectInkAtPageAsync(secondPageId, 210, 210));
                Assert.Equal(1, await adapter.EraseInkAtPageAsync(secondPageId, 210, 210, 6));
                Assert.Single(await adapter.GetInkStrokesAsync(firstPageId));

                await adapter.CommitInkStrokeAsync(secondPageId, [(230, 230), (240, 240)]);
                await adapter.AddCanvasObjectAsync(secondPageId, "Text", "Second-page note", 30, 40);
                await adapter.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.Single(await reopened.GetInkStrokesAsync(firstPageId));
            var secondPageInk = Assert.Single(await reopened.GetInkStrokesAsync(secondPageId));
            Assert.Equal(230, secondPageInk.Points[0].X);
            Assert.Empty(await reopened.GetCanvasObjectsAsync(firstPageId));
            Assert.Equal("Second-page note", Assert.Single(await reopened.GetCanvasObjectsAsync(secondPageId)).Text);

            var secondPageView = reopened.Document.Sections
                .First(section => section.Id == sectionId).Pages.First(page => page.Id == secondPageId);
            Assert.Equal(15, secondPageView.InkPanX);
            Assert.Equal(-7, secondPageView.InkPanY);
            Assert.Equal(2, secondPageView.InkZoom);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Graph_slider_definition_survives_cui_projection_save_and_reopen()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "graph-slider-projection.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            string variableId;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var page = adapter.Document.Sections[0].Pages[0];
                var graphBlock = new RichBoardBlock
                {
                    Id = "graph-slider-block", Kind = "graph", Graph = new RichGraphView()
                };
                page.Blocks.Add(graphBlock);
                adapter.MarkDirty();
                await adapter.SaveAsync();

                var viewModel = new BoardsViewModel(adapter);
                viewModel.AddGraphSlider(graphBlock.Id);
                await adapter.SaveAsync();
                var slider = Assert.Single(viewModel.FindBlock(graphBlock.Id)!.Graph!.Sliders);
                variableId = slider.VariableId;
                viewModel.ConfigureGraphSlider(graphBlock.Id, variableId, "a", -3, 7, 0.25, 1.5);
                var expression = viewModel.FindBlock(graphBlock.Id)!.Graph!.Expressions[0];
                expression.Text = "y = 2a + x";
                adapter.MarkDirty();
                await adapter.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            var restored = reopened.Document.Sections[0].Pages[0].Blocks.Single(block => block.Id == "graph-slider-block");
            var persisted = Assert.Single(restored.Graph!.Sliders);
            Assert.Equal(variableId, persisted.VariableId);
            Assert.Equal("a", persisted.Name);
            Assert.Equal(-3, persisted.Minimum);
            Assert.Equal(7, persisted.Maximum);
            Assert.Equal(0.25, persisted.Step);
            Assert.Equal(1.5, persisted.DefaultValue);
            Assert.Equal("y = 2a + x", Assert.Single(restored.Graph.Expressions).Text);
        }
        finally { DeleteDirectory(root); }
    }

    private static string TempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "cakeos-adapter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
    private static void DeleteDirectory(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
