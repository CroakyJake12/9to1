using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
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
            var uiLoader = new CuiControlLoader(BoardsCuiDocuments.CreateControlRegistry());
            uiLoader.SetBindingContext(viewModel);
            uiLoader.SetActionDispatcher(viewModel);
            var (root, diagnostics) = uiLoader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.MainSourceName));
            Assert.DoesNotContain(diagnostics, item => item.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            var control = Assert.IsAssignableFrom<Control>(root);
            uiLoader.WireBindings(control);
            viewModel.Attach(control);
            return (uiLoader, control);
        });

        Button Find(string name) => loadedRoot.GetLogicalDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == name);

        TestUiThread.Run(() =>
        {
            Assert.False(viewModel.CanSwitchBoard);
            Assert.True(Find("Current Board and board actions").IsEnabled);
            Assert.True(Find("Create a separate Board").IsEnabled);
            Assert.False(Find("Open an existing Board").IsEnabled);
            Assert.False(Find("Browse and switch Boards").IsEnabled);
        });

        await viewModel.DispatchAsync("NewBoard", null);

        TestUiThread.Run(() =>
        {
            Assert.False(viewModel.CanCreateBoard);
            Assert.True(Find("Current Board and board actions").IsEnabled);
            Assert.False(Find("Create a separate Board").IsEnabled);
            Assert.True(Find("Retry finishing the Board save").IsEnabled);
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
