using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Browse;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class UnifiedWorkspaceSessionTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Coordinator_ordinary_and_final_saves_detach_nested_split_lists_before_repository_callbacks(bool final)
    {
        var first = UnifiedWorkspaceTabState.Merge(null, Browser("First"));
        var second = UnifiedWorkspaceTabState.Merge(null, Browser("Second"));
        var outside = UnifiedWorkspaceTabState.Merge(null, Browser("Outside"));
        var panes = new List<WorkspacePaneSnapshot> { new(Guid.NewGuid(), first.Id, 0), new(Guid.NewGuid(), second.Id, 1) };
        var splitId = Guid.NewGuid();
        var splits = new List<WorkspaceSplitViewSnapshot> { new(splitId, panes, second.Id, SplitOrientation.Vertical, .5) };
        var source = new SplitSnapshotSource([first, second, outside],
            Window(WorkspaceWindowKind.Main, [first.Id, second.Id, outside.Id]) with { SelectedTabId = outside.Id, SplitViews = splits });
        var repository = new Repository { Write = (_, _) => { panes.Clear(); splits.Clear(); return Task.CompletedTask; } };
        var coordinator = new WorkspaceSessionCoordinator(repository); coordinator.Register(source, WorkspaceWindowKind.Main, queueSave: false);
        await (final ? coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None) : coordinator.SaveNowAsync(CancellationToken.None));
        var saved = Assert.Single(Assert.Single(Assert.Single(repository.Writes).Windows).SplitViews);
        Assert.Equal(splitId, saved.Id); Assert.Equal(second.Id, saved.FocusedTabId);
        Assert.Equal(new[] { first.Id, second.Id }, saved.Panes.OrderBy(pane => pane.Order).Select(pane => pane.TabId));
        Assert.Empty(panes); Assert.Empty(splits);
    }

    [AvaloniaFact]
    public async Task Saved_split_returns_after_outside_selection_and_actual_sqlite_workspace_restart()
    {
        var directory = Directory.CreateTempSubdirectory("haven-saved-split-").FullName;
        var paths = new SplitPaths(directory); var succeeded = false;
        try
        {
            var database = new SqliteDatabase(paths); await database.InitializeAsync(CancellationToken.None);
            var repository = new WorkspaceSessionRepository(database);
            var first = UnifiedWorkspaceTabState.Merge(null, Browser("Research"));
            var second = UnifiedWorkspaceTabState.Merge(null, Browser("Other profile") with { ProfileId = "other-profile" });
            var outside = UnifiedWorkspaceTabState.Merge(null, Browser("Outside"));
            var window = Window(WorkspaceWindowKind.Main, [first.Id, second.Id, outside.Id]);
            await repository.SaveAsync(new(WorkspaceSessionSnapshot.CurrentSchemaVersion, [first, second, outside], [],
                [window], DateTimeOffset.UnixEpoch), CancellationToken.None);
            var coordinator = new WorkspaceSessionCoordinator(repository);
            var session = new UnifiedWorkspaceSession(coordinator, await coordinator.LoadAsync(CancellationToken.None));
            var port = session.OpenWindow(window.Id, "profile"); Guid splitId;
            await using (var chrome = await BrowseChrome.CreateAsync(paths, homeProfileId: "profile", tabSessionPersistence: port))
            {
                await chrome.OpenSplitViewForTabsAsync(first.Id, second.Id);
                splitId = Assert.Single(chrome.State.SplitViews).Id;
                await chrome.FocusSplitPaneAsync(second.Id); await chrome.AdjustPrimaryPaneAsync(1);
                await chrome.SelectTabAsync(outside.Id);
                Assert.Empty(chrome.State.PaneTabIds); Assert.Single(chrome.State.SplitViews);
            }
            await session.SealAndDrainAsync(); await coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);
            // A new repository and connection factory read the production workspace_session payload.
            var coldRepository = new WorkspaceSessionRepository(new SqliteDatabase(paths));
            var restored = await coldRepository.LoadAsync(CancellationToken.None);
            var savedWindow = Assert.Single(restored!.Windows); var savedSplit = Assert.Single(savedWindow.SplitViews);
            Assert.Equal(splitId, savedSplit.Id); Assert.Equal(outside.Id, savedWindow.SelectedTabId);
            Assert.Equal(WorkspaceLayoutKind.Single, savedWindow.Layout.Kind); Assert.Equal(3, restored.Tabs.Count);
            var coldCoordinator = new WorkspaceSessionCoordinator(coldRepository);
            var coldSession = new UnifiedWorkspaceSession(coldCoordinator, await coldCoordinator.LoadAsync(CancellationToken.None));
            var coldPort = coldSession.OpenWindow(window.Id, "profile");
            await using (var chrome = await BrowseChrome.CreateAsync(paths, homeProfileId: "profile", tabSessionPersistence: coldPort))
            {
                Assert.Equal(outside.Id, chrome.State.SelectedTabId); Assert.Empty(chrome.State.PaneTabIds);
                await chrome.SelectSplitViewAsync(splitId);
                Assert.Equal(second.Id, chrome.State.SelectedTabId);
                Assert.Equal(new[] { first.Id, second.Id }, chrome.State.PaneTabIds);
                Assert.Equal(.55, chrome.State.PrimaryPaneRatio, precision: 2);
                Assert.Equal("profile", chrome.State.Tabs.Single(tab => tab.Id == first.Id).ProfileId);
                Assert.Equal("other-profile", chrome.State.Tabs.Single(tab => tab.Id == second.Id).ProfileId);
                await chrome.CloseSplitViewAsync(); Assert.Empty(chrome.State.SplitViews); Assert.Equal(3, chrome.State.Tabs.Count);
            }
            await coldSession.SealAndDrainAsync(); await coldCoordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);
            Assert.Empty(Assert.Single((await coldRepository.LoadAsync(CancellationToken.None))!.Windows).SplitViews);
            succeeded = true;
        }
        finally
        {
            // Preserve the actual database and WAL if the owning regression fails.
            if (succeeded) Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Queued_split_save_detaches_producer_lists_before_the_actual_prior_write_settles()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Repository { Write = (_, count) =>
        {
            if (count != 1) return Task.CompletedTask;
            entered.SetResult(); return release.Task;
        } };
        var coordinator = new WorkspaceSessionCoordinator(repository);
        var session = new UnifiedWorkspaceSession(coordinator, null);
        var port = session.OpenWindow(session.MainWindowId, "profile");
        var a = Browser("A"); var b = Browser("B"); var outside = Browser("Outside");
        var firstSave = port.SaveAsync(new([a, b, outside], outside.Id), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var paneIds = new List<Guid> { a.Id, b.Id }; var id = Guid.NewGuid();
        var splits = new List<BrowseSplitViewProjection> { new(id, paneIds, b.Id, SplitOrientation.Vertical, .5) };
        var queued = port.SaveAsync(new([a, b, outside], outside.Id) { PaneTabIds = [], SplitViews = splits }, CancellationToken.None);
        paneIds[0] = outside.Id; splits.Clear();
        Assert.False(queued.IsCompleted); Assert.Single(repository.Writes);
        release.SetResult(); await Task.WhenAll(firstSave, queued).WaitAsync(TimeSpan.FromSeconds(5));
        await session.SealAndDrainAsync(); await coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);
        var saved = Assert.Single(Assert.Single(repository.Writes[^1].Windows).SplitViews);
        Assert.Equal(id, saved.Id); Assert.Equal(b.Id, saved.FocusedTabId);
        Assert.Equal(new[] { a.Id, b.Id }, saved.Panes.OrderBy(pane => pane.Order).Select(pane => pane.TabId));
    }

    [AvaloniaFact]
    public async Task Browse_group_operations_update_the_existing_canonical_group_identity_and_membership()
    {
        var groupId = Guid.NewGuid();
        var first = UnifiedWorkspaceTabState.Merge(null, Browser("First") with { Group = groupId.ToString("D") }) with { GroupId = groupId };
        var second = UnifiedWorkspaceTabState.Merge(null, Browser("Second") with
            { Group = groupId.ToString("D"), ProfileId = "other-profile" }) with { GroupId = groupId };
        var group = new TabGroupSnapshot(groupId, "Research", false, [first.Id, second.Id]);
        var window = Window(WorkspaceWindowKind.Main, [first.Id, second.Id]);
        var repository = new Repository { Restored = new(WorkspaceSessionSnapshot.CurrentSchemaVersion,
            [first, second], [group], [window], DateTimeOffset.UnixEpoch) };
        var coordinator = new WorkspaceSessionCoordinator(repository);
        var session = new UnifiedWorkspaceSession(coordinator, await coordinator.LoadAsync(CancellationToken.None));
        var port = session.OpenWindow(window.Id, "profile");

        var loaded = await port.LoadAsync(CancellationToken.None) ?? throw new InvalidOperationException("Group projection was unavailable.");
        var restoredGroup = Assert.Single(loaded.Groups!);
        Assert.Equal(groupId, restoredGroup.Id); Assert.Equal(new[] { first.Id, second.Id }, restoredGroup.OrderedTabIds);
        var changed = loaded with { Groups = [restoredGroup with
            { Name = "Project notes", IsCollapsed = true, OrderedTabIds = [second.Id, first.Id] }] };
        await port.SaveAsync(changed with { Tabs = [loaded.Tabs[1] with { Group = groupId.ToString("D") },
            loaded.Tabs[0] with { Group = groupId.ToString("D") }] }, CancellationToken.None);
        await session.SealAndDrainAsync();
        await coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);

        var final = repository.Writes[^1];
        var savedGroup = Assert.Single(final.Groups);
        Assert.Equal(groupId, savedGroup.Id); Assert.Equal("Project notes", savedGroup.Name); Assert.True(savedGroup.IsCollapsed);
        Assert.Equal(new[] { second.Id, first.Id }, savedGroup.OrderedTabIds);
        Assert.All(final.Tabs, tab => Assert.Equal(groupId, tab.GroupId));
        Assert.Equal("profile", UnifiedWorkspaceTabState.TryProject(final.Tabs.Single(tab => tab.Id == first.Id), "profile", out var projectedFirst)
            ? projectedFirst!.ProfileId : null);
        Assert.Equal("other-profile", UnifiedWorkspaceTabState.TryProject(final.Tabs.Single(tab => tab.Id == second.Id), "profile", out var projectedSecond)
            ? projectedSecond!.ProfileId : null);
    }

    [AvaloniaFact]
    public async Task Browse_split_panes_restore_and_round_trip_through_the_existing_window_layout()
    {
        var first = UnifiedWorkspaceTabState.Merge(null, Browser("First"));
        var second = UnifiedWorkspaceTabState.Merge(null, Browser("Second") with { ProfileId = "other-profile" });
        var window = Window(WorkspaceWindowKind.Main, [first.Id, second.Id]) with
        {
            SelectedTabId = second.Id,
            Layout = new WorkspaceLayoutSnapshot(Guid.NewGuid(), WorkspaceLayoutKind.Split,
                SplitOrientation.Horizontal, 0.5,
                [new(Guid.NewGuid(), first.Id, 0), new(Guid.NewGuid(), second.Id, 1)])
        };
        var repository = new Repository
        {
            Restored = new(WorkspaceSessionSnapshot.CurrentSchemaVersion, [first, second], [], [window], DateTimeOffset.UnixEpoch)
        };
        var coordinator = new WorkspaceSessionCoordinator(repository);
        var session = new UnifiedWorkspaceSession(coordinator, await coordinator.LoadAsync(CancellationToken.None));
        var port = session.OpenWindow(window.Id, "profile");

        var loaded = await port.LoadAsync(CancellationToken.None);
        Assert.Equal(new[] { first.Id, second.Id }, loaded!.PaneTabIds);
        Assert.Equal("profile", loaded.Tabs.Single(tab => tab.Id == first.Id).ProfileId);
        Assert.Equal("other-profile", loaded.Tabs.Single(tab => tab.Id == second.Id).ProfileId);
        await port.SaveAsync(loaded with { PaneTabIds = [second.Id, first.Id],
            SplitOrientation = SplitOrientation.Vertical, PrimaryPaneRatio = 0.65,
            // The same typed operation updates the durable object AND its active projection.
            SplitViews = [Assert.Single(loaded.SplitViews!) with { PaneTabIds = [second.Id, first.Id],
                Orientation = SplitOrientation.Vertical, PrimaryPaneRatio = 0.65 }] }, CancellationToken.None);
        var saved = repository.Writes[^1].Windows.Single();
        Assert.Equal(WorkspaceLayoutKind.Split, saved.Layout.Kind);
        Assert.Equal(SplitOrientation.Vertical, saved.Layout.Orientation);
        Assert.Equal(0.65, saved.Layout.PrimaryRatio, precision: 2);
        Assert.Equal(new[] { second.Id, first.Id }, saved.Layout.Panes.OrderBy(pane => pane.Order).Select(pane => pane.TabId));
        Assert.True(UnifiedWorkspaceTabState.TryProject(repository.Writes[^1].Tabs.Single(tab => tab.Id == second.Id),
            "profile", out var restoredOtherProfile));
        Assert.Equal("other-profile", restoredOtherProfile!.ProfileId);
        Assert.Equal(window.Layout.Panes.Select(pane => pane.Id).OrderBy(id => id),
            saved.Layout.Panes.Select(pane => pane.Id).OrderBy(id => id));
        await session.SealAndDrainAsync();
    }

    [AvaloniaFact]
    public async Task First_projection_keeps_unopened_windows_and_unrepresentable_rows_in_canonical_writer()
    {
        var first = Row("home", "{}");
        var unknown = Row("spaces", "{\"9to1.AppContent\":{\"Version\":99}}");
        var other = Row("files", "{\"extensionState\":{\"keep\":true}}");
        var main = Window(WorkspaceWindowKind.Main, [first.Id, unknown.Id]);
        var second = Window(WorkspaceWindowKind.Normal, [other.Id]);
        var baseline = new WorkspaceSessionSnapshot(WorkspaceSessionSnapshot.CurrentSchemaVersion,
            [first, unknown, other], [], [main, second], DateTimeOffset.UnixEpoch);
        var repository = new Repository { Restored = baseline };
        var coordinator = new WorkspaceSessionCoordinator(repository);
        var session = new UnifiedWorkspaceSession(coordinator, await coordinator.LoadAsync(CancellationToken.None));
        Assert.Empty(repository.Writes);
        var port = session.OpenWindow(main.Id, "profile");
        var loaded = await port.LoadAsync(CancellationToken.None);
        var projected = Assert.Single(loaded!.Tabs);
        Assert.Equal(first.Id, projected.Id);

        await port.SaveAsync(new([projected with { Title = "Home renamed" }], first.Id), CancellationToken.None);
        await session.SealAndDrainAsync();
        await coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);

        var final = repository.Writes[^1];
        Assert.Equal(3, final.Tabs.Count);
        Assert.Equal(unknown, final.Tabs.Single(tab => tab.Id == unknown.Id));
        Assert.Equal(other, final.Tabs.Single(tab => tab.Id == other.Id));
        var preservedWindow = final.Windows.Single(window => window.Id == second.Id);
        Assert.Equal(second.BoundsJson, preservedWindow.BoundsJson);
        Assert.Equal(second.OrderedTabIds, preservedWindow.OrderedTabIds);
        Assert.Equal(second.Layout.Id, preservedWindow.Layout.Id);
        Assert.Equal("Home renamed", final.Tabs.Single(tab => tab.Id == first.Id).Title);
        Assert.Equal(new[] { first.Id, unknown.Id }, final.Windows.Single(window => window.Id == main.Id).OrderedTabIds);
        Assert.Equal(1, repository.Loads);
    }

    [AvaloniaFact]
    public async Task Recently_closed_tabs_load_and_reopen_through_the_canonical_window_owner()
    {
        var active = UnifiedWorkspaceTabState.Merge(null, Browser("Active"));
        var closedId = Guid.NewGuid();
        var closed = new WorkspaceClosedTabSnapshot(closedId, "Saved research", "https://research.example/",
            "other-profile", null, 2, "Chromium", true, DateTimeOffset.UnixEpoch.AddDays(2));
        var main = Window(WorkspaceWindowKind.Main, [active.Id]) with { RecentlyClosedTabs = [closed] };
        var baseline = new WorkspaceSessionSnapshot(WorkspaceSessionSnapshot.CurrentSchemaVersion,
            [active], [], [main], DateTimeOffset.UnixEpoch);
        var repository = new Repository { Restored = baseline };
        var coordinator = new WorkspaceSessionCoordinator(repository);
        var session = new UnifiedWorkspaceSession(coordinator, await coordinator.LoadAsync(CancellationToken.None));
        var port = session.OpenWindow(main.Id, "current-profile");

        var loaded = await port.LoadAsync(CancellationToken.None)
            ?? throw new InvalidOperationException("The existing session owner returned no projection.");
        Assert.Equal(closedId, Assert.Single(loaded.ClosedTabs).Tab.Id);
        Assert.Equal("other-profile", loaded.ClosedTabs[0].Tab.ProfileId);
        await port.SaveAsync(loaded, CancellationToken.None);
        Assert.Equal("Chromium", Assert.Single(repository.Writes[0].Windows.Single().RecentlyClosedTabs).EngineOverride);
        await port.SaveAsync(new BrowseTabSessionProjection(
            loaded.Tabs.Append(loaded.ClosedTabs[0].Tab).ToArray(), closedId), CancellationToken.None);
        await session.SealAndDrainAsync();
        await coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);

        var final = repository.Writes[^1];
        var window = Assert.Single(final.Windows);
        Assert.Empty(window.RecentlyClosedTabs);
        Assert.Contains(closedId, window.OrderedTabIds);
        var reopened = final.Tabs.Single(tab => tab.Id == closedId);
        Assert.True(UnifiedWorkspaceTabState.TryProject(reopened, "current-profile", out var projected));
        Assert.Equal("https://research.example/", projected!.Address);
        Assert.Equal("other-profile", projected!.ProfileId);
    }

    [AvaloniaFact]
    public async Task Held_actual_write_serializes_later_projection_and_drain_preserves_both_before_final_capture()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Repository();
        repository.Write = (_, count) =>
        {
            if (count != 1) return Task.CompletedTask;
            entered.SetResult();
            return release.Task;
        };
        var coordinator = new WorkspaceSessionCoordinator(repository);
        var session = new UnifiedWorkspaceSession(coordinator, null);
        var port = session.OpenWindow(session.MainWindowId, "profile");
        var tab = Browser("First");
        var firstSave = port.SaveAsync(new([tab], tab.Id), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondSave = port.SaveAsync(new([tab with { Title = "Second" }], tab.Id), CancellationToken.None);
        var drain = session.SealAndDrainAsync();
        Assert.Same(drain, session.SealAndDrainAsync());
        Assert.False(firstSave.IsCompleted);
        Assert.False(secondSave.IsCompleted);
        Assert.False(drain.IsCompleted);
        Assert.Single(repository.Writes);
        Assert.Throws<InvalidOperationException>(() => { _ = port.SaveAsync(new([tab], tab.Id), CancellationToken.None); });

        release.SetResult();
        await Task.WhenAll(firstSave, secondSave, drain).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "First", "Second" }, repository.Writes.Select(snapshot => Assert.Single(snapshot.Tabs).Title));
        await coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);
        Assert.Equal("Second", Assert.Single(repository.Writes[^1].Tabs).Title);
        Assert.Equal(3, repository.Writes.Count);
    }

    [AvaloniaFact]
    public async Task Coordinator_terminal_seal_also_refuses_late_mutation_if_host_omits_projection_seal()
    {
        var repository = new Repository();
        var coordinator = new WorkspaceSessionCoordinator(repository);
        var session = new UnifiedWorkspaceSession(coordinator, null);
        var port = session.OpenWindow(session.MainWindowId, "profile");
        var tab = Browser("Saved");
        await port.SaveAsync(new([tab], tab.Id), CancellationToken.None);
        await coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);
        Assert.Throws<InvalidOperationException>(() =>
        { _ = port.SaveAsync(new([tab with { Title = "Too late" }], tab.Id), CancellationToken.None); });
        Assert.Equal("Saved", Assert.Single(repository.Writes[^1].Tabs).Title);
        await session.SealAndDrainAsync();
    }

    [AvaloniaFact]
    public async Task Failed_save_is_retained_even_when_a_later_user_save_succeeds()
    {
        var failure = new IOException("Actual canonical repository failure");
        var repository = new Repository { Write = (_, count) => count == 1 ? Task.FromException(failure) : Task.CompletedTask };
        var coordinator = new WorkspaceSessionCoordinator(repository);
        var session = new UnifiedWorkspaceSession(coordinator, null);
        var port = session.OpenWindow(session.MainWindowId, "profile");
        var tab = Browser("First");
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() =>
            port.SaveAsync(new([tab], tab.Id), CancellationToken.None)));
        await port.SaveAsync(new([tab with { Title = "Retry" }], tab.Id), CancellationToken.None);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => session.SealAndDrainAsync()));
        Assert.Equal("Retry", Assert.Single(repository.Writes[^1].Tabs).Title);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() =>
            coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None)));
    }

    [AvaloniaFact]
    public async Task Repository_callback_cannot_join_or_enqueue_its_own_projection_operation()
    {
        var repository = new Repository();
        var coordinator = new WorkspaceSessionCoordinator(repository);
        var session = new UnifiedWorkspaceSession(coordinator, null);
        var port = session.OpenWindow(session.MainWindowId, "profile");
        var tab = Browser("Current");
        repository.Write = (_, _) =>
        {
            Assert.Throws<InvalidOperationException>(() => { _ = session.SealAndDrainAsync(); });
            Assert.Throws<InvalidOperationException>(() => { _ = port.SaveAsync(new([tab], tab.Id), CancellationToken.None); });
            return Task.CompletedTask;
        };
        await port.SaveAsync(new([tab], tab.Id), CancellationToken.None);
        await session.SealAndDrainAsync();
        Assert.Single(repository.Writes);
    }

    private static BrowserTabState Browser(string title) => new(
        Guid.NewGuid(), title, "https://example.org/", BrowserTabPrivacy.Standard,
        string.Empty, DateTimeOffset.UnixEpoch) { ProfileId = "profile" };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Saved_Files_board_state_keeps_identity_but_never_prepared_capability(bool writable)
    {
        var token = Guid.NewGuid();
        var prepared = writable
            ? BoardsBrowseAppContentAddress.ForWritableFile(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), token)
            : BoardsBrowseAppContentAddress.ForFile(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), token);
        var tab = Browser("Saved board") with { Address = prepared.Value };
        var stored = UnifiedWorkspaceTabState.Merge(null, tab);
        Assert.DoesNotContain(token.ToString("D"), stored.StateJson, StringComparison.Ordinal);
        Assert.True(UnifiedWorkspaceTabState.TryProject(stored, "profile", out var restored));
        var durable = BoardsBrowseAppContentAddress.Parse(AppContentAddress.Parse(restored!.Address));
        var original = BoardsBrowseAppContentAddress.Parse(prepared);
        Assert.Null(durable.ReadToken);
        Assert.Equal(original.FileId, durable.FileId);
        Assert.Equal(original.ParentFolderId, durable.ParentFolderId);
        Assert.Equal(original.RevisionId, durable.RevisionId);
        Assert.Equal(writable, durable.Writable);
        // A historical on-disk row with the old token is migrated on read as well.
        var old = stored with { StateJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        { ["9to1.AppContent"] = new { Version = 1, Address = prepared.Value, ProfileId = "profile" } }) };
        Assert.True(UnifiedWorkspaceTabState.TryProject(old, "profile", out var migrated));
        Assert.Equal(restored.Address, migrated!.Address);
    }

    [Theory]
    [InlineData("9to1://boards/future-format/document")]
    [InlineData("9to1://boards/files/not-a-prepared-file")]
    public void Failed_or_future_Boards_routes_remain_persistable_without_breaking_window_close(string address)
    {
        var tab = Browser("Failed Board") with { Address = address };
        var stored = UnifiedWorkspaceTabState.Merge(null, tab);
        Assert.True(UnifiedWorkspaceTabState.TryProject(stored, "profile", out var restored));
        Assert.Equal(address, restored!.Address);
        var roundTrip = UnifiedWorkspaceTabState.Merge(stored, restored);
        Assert.Equal(stored.StateJson, roundTrip.StateJson);
    }
    private sealed class SplitPaths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
    private sealed class SplitSnapshotSource(IReadOnlyList<TabSessionSnapshot> tabs, WorkspaceWindowSnapshot window) : IWorkspaceSessionSource
    {
        public bool IsDisposed => false;
        public IReadOnlyList<TabSessionSnapshot> CreateTabSnapshots() => tabs;
        public IReadOnlyList<TabGroupSnapshot> CreateGroupSnapshots() => [];
        public IReadOnlyList<WorkspaceWindowSnapshot> CreateWindowSnapshots(WorkspaceWindowKind kind) => [window];
    }
    private static TabSessionSnapshot Row(string appKey, string state) => new(
        Guid.NewGuid(), appKey, "Saved", state, "{\"navigation\":true}", 42, null,
        true, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    private static WorkspaceWindowSnapshot Window(WorkspaceWindowKind kind, Guid[] ids) => new(
        Guid.NewGuid(), kind, new(Guid.NewGuid(), WorkspaceLayoutKind.Single, SplitOrientation.Vertical,
            0.6, [new(Guid.NewGuid(), ids[0], 0)]), ids, ids[0], "{\"width\":1234}", DateTimeOffset.UnixEpoch);

    private sealed class Repository : IWorkspaceSessionRepository
    {
        internal WorkspaceSessionSnapshot? Restored;
        internal int Loads;
        internal readonly List<WorkspaceSessionSnapshot> Writes = [];
        internal Func<WorkspaceSessionSnapshot, int, Task>? Write;
        public Task<WorkspaceSessionSnapshot?> LoadAsync(CancellationToken cancellationToken)
        { Loads++; return Task.FromResult(Restored); }
        public Task SaveAsync(WorkspaceSessionSnapshot snapshot, CancellationToken cancellationToken)
        { Writes.Add(snapshot); return Write?.Invoke(snapshot, Writes.Count) ?? Task.CompletedTask; }
    }
}
