using Avalonia.Controls;
using CakeOS.Apps.Boards.App;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsHomeBoardSwitcherTests
{
    [Fact]
    public async Task Home_board_menu_remains_open_for_create_and_recovery_when_switching_is_not_allowed()
    {
        var now = DateTimeOffset.UtcNow;
        var document = new HavenBoardDocument(HavenBoardDocument.FormatIdentity,
            HavenBoardDocument.CurrentSchemaVersion, Guid.NewGuid(), now, now,
            Snapshot: null, RichNotes: HavenRichNotes.Create("Pinned Files Board"));
        await using var adapter = await ContractSessionAdapter.OpenExternallyWritableAsync(
            document, (_, _) => ValueTask.FromResult(true));
        var host = new PendingCreateHost();
        var viewModel = new BoardsViewModel(adapter, homeBoardCreationHost: host);
        var (loader, loadedRoot) = TestUiThread.Run(() =>
        {
            var uiLoader = BoardsCuiDocuments.CreateLoader();
            uiLoader.SetBindingContext(viewModel);
            uiLoader.SetActionDispatcher(viewModel);
            var (root, diagnostics) = uiLoader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.MainSourceName));
            Assert.DoesNotContain(diagnostics, item => item.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            var control = Assert.IsAssignableFrom<Control>(root);
            uiLoader.WireBindings(control);
            viewModel.Attach(control);
            return (uiLoader, control);
        });

        Control Find(string id)
        {
            var authored = BoardsCuiControlFinder.FindByAutomationId<Control>(loadedRoot, id)
                ?? throw new InvalidOperationException("Missing authored menu item " + id + ": " +
                    BoardsCuiControlFinder.DescribeRuntimeTree(loadedRoot));
            return authored is CuiContainerControl container ? container.Layout : authored;
        }

        TestUiThread.Run(() =>
        {
            var switcher = BoardsCuiControlFinder.FindByAutomationId<MenuItem>(loadedRoot, "BoardSwitcher");
            Assert.NotNull(switcher);
            var title = BoardsCuiControlFinder.FindByAutomationId<TextBox>(loadedRoot, "BoardTitleBox");
            Assert.NotNull(title);
            Assert.Equal("Pinned Files Board", title!.Text);
            viewModel.EditText("BoardTitleBox", "Renamed Files Board");
            Assert.Equal("Renamed Files Board", title.Text);
        });

        TestUiThread.Run(() =>
        {
            Assert.False(viewModel.CanSwitchBoard);
            Assert.True(Find("BoardSwitcher").IsEnabled);
            Assert.True(Find("BoardSwitcherCreate").IsEnabled);
            Assert.False(Find("BoardSwitcherOpen").IsEnabled);
            Assert.False(Find("BoardSwitcherAll").IsEnabled);
        });

        // Dispatch from the UI owner thread so the authored live bindings are
        // refreshed synchronously, as they are for a real CUI menu action.
        TestUiThread.Run(() => viewModel.DispatchAsync("NewBoard", null).GetAwaiter().GetResult());

        TestUiThread.Run(() =>
        {
            Assert.False(viewModel.CanCreateBoard);
            Assert.True(Find("BoardSwitcher").IsEnabled);
            Assert.False(Find("BoardSwitcherCreate").IsEnabled);
            Assert.True(Find("BoardSwitcherRetrySave").IsEnabled);
        });
        TestUiThread.Run(() =>
        {
            viewModel.Dispose();
            loader.Dispose();
        });
    }

    private sealed class PendingCreateHost : IBoardsHomeBoardCreationHost
    {
        private bool _pending;
        public bool CanRetryOpenCreatedBoard => false;
        public bool CanRetryCreationSettlement => _pending;
        public bool CanInspectUnknownCreation => false;
        public bool CanOpenRecordedBoard => false;
        public bool HasUnresolvedCreation => _pending;

        public Task<BoardsHomeBoardCreationResult> CreateAndOpenAsync(ReadOnlyMemory<byte> validatedDraft,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _pending = true;
            return Task.FromResult(new BoardsHomeBoardCreationResult(
                BoardsHomeBoardCreationOutcome.HomeAuditPending));
        }

        public Task<BoardsHomeBoardCreationResult> RetryCreationSettlementAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.HomeAuditPending));
        public Task<BoardsHomeBoardCreationResult> InspectUnknownCreationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown));
        public Task<BoardsHomeBoardCreationResult> OpenRecordedBoardAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown));
        public Task<BoardsHomeBoardCreationResult> RetryOpenCreatedBoardAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.CreatedOpenFailed));
    }
}
