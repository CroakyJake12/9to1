using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Data.RecordsUI;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsTabberDraftTests
{
    [Theory]
    [InlineData("tabber")]
    [InlineData("freeform")]
    public async Task Hidden_pane_field_draft_refuses_replacement_and_keeps_the_same_live_editor(string replacement)
    {
        await using var data = await OpenData();
        var definition = data.Snapshot.RecordViews[0];
        var reference = new HavenBoardDatabaseReference(data.Reference.StoreID, data.Reference.WorkbookID, definition.TableID, definition.ViewID);
        await BoardsRelativeInsertionTests.Run(async (adapter, _) =>
        {
            var vm = TestUiThread.Run(() => new BoardsViewModel(adapter, databaseSource: new Source(data)));
            var owner = vm.PageBlocks.First(block => block.Kind == "tabber");
            var selected = owner.Tabber!.SelectedPaneId;
            Window? window = null; DataRecordViewControl? view = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    window = new Window { Width = 1100, Height = 750, Content = vm.Tabbers.Mount(owner) }; window.Show();
                    BoardsTabberLifetimeTests.Settle(window, () => (view = window.GetVisualDescendants().OfType<DataRecordViewControl>().FirstOrDefault()) is not null);
                    var input = view!.GetVisualDescendants().OfType<TextBox>().Single(box => box.Text == "Saved record value");
                    input.Text = "Keep my unsaved field";
                    BoardsTabberLifetimeTests.Settle(window, () => view!.HasUnsavedDrafts);
                });
                BoardsTabberInsertionTests.OnUi(() => vm.ChangeTabberAsync(owner.Id, "select", owner.Tabber!.Panes[0].Id));
                TestUiThread.Run(() => window!.Content = vm.Tabbers.Mount(vm.PageBlocks.Single(block => block.Id == owner.Id)));
                Assert.True(await Task.Run(() => vm.HasUnsettledDatabaseWork));
                var before = adapter.Snapshot; var version = adapter.DocumentVersion;
                Assert.Null(vm.RelativeInsertionUnavailableReason(replacement, owner.Id, "replace"));
                BoardsTabberInsertionTests.OnUi(() => vm.ReplaceBlockWithKindAsync(replacement, owner.Id));
                Assert.Equal(before, adapter.Snapshot); Assert.Equal(version, adapter.DocumentVersion);
                TestUiThread.Run(() => Assert.True(view!.HasUnsavedDrafts));
                BoardsTabberInsertionTests.OnUi(() => vm.ChangeTabberAsync(owner.Id, "select", selected));
                TestUiThread.Run(() =>
                {
                    window!.Content = vm.Tabbers.Mount(vm.PageBlocks.Single(block => block.Id == owner.Id));
                    BoardsTabberLifetimeTests.Settle(window, () => window.GetVisualDescendants().OfType<DataRecordViewControl>().Any());
                    Assert.Same(view, window.GetVisualDescendants().OfType<DataRecordViewControl>().Single());
                    Assert.Contains(view!.GetVisualDescendants().OfType<TextBox>(), box => box.Text == "Keep my unsaved field");
                    view!.DiscardDrafts(); Assert.False(view.HasUnsavedDrafts);
                });
                Assert.False(await Task.Run(() => vm.HasUnsettledDatabaseWork));
                BoardsTabberInsertionTests.OnUi(() => vm.ReplaceBlockWithKindAsync(replacement, owner.Id));
                Assert.DoesNotContain(vm.PageBlocks, block => block.Id == owner.Id);
            }
            finally
            {
                TestUiThread.Run(() => { view?.DiscardDrafts(); window?.Close(); });
                var disposal = Task.Run(async () => await vm.DisposeDatabasesAsync());
                BoardsTabberInsertionTests.OnUi(() => disposal);
                TestUiThread.Run(vm.Dispose);
            }
        }, rich =>
        {
            BoardsTabberInsertionTests.SeedTabbers(rich);
            var owner = rich.Sections[0].Pages[0].Blocks.First(block => block.Kind == HavenRichBlockKind.Tabber);
            if (replacement == "freeform")
            {
                owner.Layout = HavenRichBlockLayouts.Absolute;
                owner.X = 80; owner.Y = 140; owner.Width = 600; owner.Height = 400;
            }
            var selected = owner.Tabber!.Panes.Single(pane => pane.Id == owner.Tabber.SelectedPaneId);
            rich.Sections[0].Pages.Single(page => page.Id == selected.PageId).Blocks =
                [new HavenRichBlock { Kind = HavenRichBlockKind.Database, Database = reference }];
        });
    }

    [Fact]
    public async Task Pending_pane_database_creation_and_unlinked_artifact_both_refuse_replacement()
    {
        await using var data = await OpenData();
        var source = new Source(data, delayed: true);
        await BoardsRelativeInsertionTests.Run(async (adapter, _) =>
        {
            var vm = TestUiThread.Run(() => new BoardsViewModel(adapter, databaseSource: source));
            var parentId = vm.SelectedPageId;
            var owner = vm.PageBlocks.First(block => block.Kind == "tabber");
            Window? window = null; Task? creating = null; Action? refreshToolbar = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    // This Data-port component fixture now supplies the one host toolbar;
                    // a pane no longer owns a duplicate. Keep the real insertion menu
                    // and all pending-artifact/owner-loss assertions below unchanged.
                    var toolbar = new StackPanel();
                    refreshToolbar = () => ToolbarBuilder.Rebuild(toolbar, vm);
                    vm.ToolbarChanged += refreshToolbar;
                    var content = new StackPanel(); content.Children.Add(toolbar); content.Children.Add(vm.Tabbers.Mount(owner));
                    window = new Window { Width = 1100, Height = 750, Content = content }; window.Show();
                    BoardsTabberLifetimeTests.Settle(window, () => window.GetVisualDescendants().OfType<TextBox>().Any(box =>
                        AutomationProperties.GetName(box) == "Paragraph editor"));
                    window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Paragraph editor").Focus();
                    refreshToolbar();
                    Button? insert = null;
                    BoardsTabberLifetimeTests.Settle(window, () => (insert = toolbar.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
                        ToolbarBuilder.HasInsertMenu(button) && ToolbarBuilder.InsertMenuFor(button)?.AnchorBlockId is null)) is not null);
                    var menu = Assert.IsType<InsertMenuContext>(ToolbarBuilder.InsertMenuFor(insert!));
                    Assert.True(menu.Open()); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    var create = menu.Popup.Child!.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Insert New database");
                    create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
                    Assert.Equal(1, source.Creates); creating = menu.WhenActionsIdleAsync(); Assert.False(creating.IsCompleted);
                });
                var before = adapter.Snapshot;
                BoardsTabberInsertionTests.OnUi(() => vm.ReplaceBlockWithKindAsync("paragraph", owner.Id));
                Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
                TestUiThread.Run(() => vm.SelectPageById("other"));
                source.Creation.SetResult(data); BoardsTabberInsertionTests.OnUi(() => creating!);
                TestUiThread.Run(() => vm.SelectPageById(parentId));
                Assert.True(vm.HasUnsettledDatabaseWork);
                BoardsTabberInsertionTests.OnUi(() => vm.ReplaceBlockWithKindAsync("tabber", owner.Id));
                Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
                Assert.NotEmpty(data.Snapshot.RecordViews); // returned artifact still has a live owner
            }
            finally
            {
                source.Creation.TrySetResult(data);
                if (creating is not null) BoardsTabberInsertionTests.OnUi(() => creating);
                TestUiThread.Run(() => { vm.ToolbarChanged -= refreshToolbar; window?.Close(); });
                BoardsTabberInsertionTests.OnUi(() => vm.DisposeDatabasesAsync().AsTask());
                TestUiThread.Run(vm.Dispose);
            }
        }, rich =>
        {
            BoardsTabberInsertionTests.SeedTabbers(rich);
            rich.Sections[0].Pages.Add(new HavenRichPage { Id = "other", Title = "Other" });
        });
    }

    private static async Task<DataRecordSession> OpenData()
    {
        var workbook = DataRecordSession.NewRecordDatabase("Pane draft fixture");
        var table = workbook.Tables.Single(); var field = table.RelationalSchema!.Fields.Single(field => field.Name == "Name");
        workbook = DataRecordCreation.Prepare(workbook, table.Id, Guid.NewGuid(), workbook.Version, workbook.RevisionId,
            new Dictionary<Guid, DataScalarRecordValue> { [field.FieldID] = new(DataCellKind.Text, JsonSerializer.SerializeToElement("Saved record value")) }).Workbook!;
        var repository = new Repository(workbook);
        var session = new DataRecordSession(repository, new Authority(), new Actors());
        await session.OpenAsync(new(repository.Store, workbook.Id, workbook.Version, workbook.RevisionId)); return session;
    }
    private sealed class Source(DataRecordSession data, bool delayed = false) : IBoardsDatabaseSource
    {
        internal int Creates;
        internal TaskCompletionSource<DataRecordSession> Creation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DataRecordSession> CreateDatabaseAsync(string title, CancellationToken token = default)
        { Creates++; return delayed ? Creation.Task : Task.FromResult(data); }
        public Task<DataRecordSession> OpenDatabaseAsync(Guid id, CancellationToken token = default) => Task.FromResult(data);
    }
    // Only repository/authority ports are substituted. The Data session, canonical record,
    // production RecordsUI draft and Boards pane/cache/admission paths are real headless components.
    private sealed class Repository(DataWorkbook workbook) : IDataGuardedWorkbookRepository
    {
        internal Guid Store { get; } = Guid.NewGuid();
        public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token) => ValueTask.FromResult(new ResourceStoreIdentity(1, Store, DateTimeOffset.UtcNow, false));
        public ValueTask<DataWorkbookStoreEvidence?> ReadStoreEvidenceAsync(CancellationToken token) => ValueTask.FromResult<DataWorkbookStoreEvidence?>(null);
        public Task<DataWorkbook?> LoadAsync(Guid id, CancellationToken token) => Task.FromResult<DataWorkbook?>(workbook);
        public Task<IReadOnlyList<DataWorkbookSummary>> ListAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<DataSaveResult> SaveAsync(DataWorkbook value, string reason, CancellationToken token) => throw new NotSupportedException();
        public Task<DataSaveResult> SaveAsync(DataWorkbook value, string reason, IDataWorkbookCommitAdmission admission, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) =>
            ValueTask.FromResult<AuthenticatedResourceActor?>(new("local:fixture", "profile", null, null, "fixture-session"));
    }
    private sealed class Authority : IDataWorkbookCommitAuthority, IDataWorkbookCommitAdmission
    {
        public ValueTask<IDataWorkbookCommitAdmission?> CaptureAsync(Guid store, Guid workbook, int version, Guid revision,
            string action, AuthenticatedResourceActor actor, CancellationToken token) => ValueTask.FromResult<IDataWorkbookCommitAdmission?>(this);
        public ValueTask<bool> CheckAsync(DataWorkbookCommitContext context, CancellationToken token) => ValueTask.FromResult(true);
    }
}
