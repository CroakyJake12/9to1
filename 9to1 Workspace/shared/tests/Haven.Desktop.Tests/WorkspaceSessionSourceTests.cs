using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class WorkspaceSessionSourceTests
{
    [AvaloniaFact]
    public async Task Unified_source_uses_existing_final_writer_and_preserves_all_workspace_metadata()
    {
        var repository = new Repository();
        var coordinator = new WorkspaceSessionCoordinator(repository);
        var source = new Source();
        coordinator.Register(source, WorkspaceWindowKind.Main, queueSave: false);

        var close = coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);
        coordinator.Unregister(source);
        await close;

        var saved = Assert.Single(repository.Writes);
        Assert.Equal(WorkspaceSessionSnapshot.CurrentSchemaVersion, saved.SchemaVersion);
        Assert.Equal(new[] { source.Tab, source.SecondTab }, saved.Tabs);
        var group = Assert.Single(saved.Groups);
        Assert.Equal(source.Group.Id, group.Id);
        Assert.Equal(source.Group.Name, group.Name);
        Assert.True(group.IsCollapsed);
        Assert.Equal(source.Group.OrderedTabIds, group.OrderedTabIds);
        Assert.Equal(2, saved.Windows.Count);
        var window = saved.Windows[0];
        Assert.Equal(source.Window.Id, window.Id);
        Assert.Equal(source.Window.BoundsJson, window.BoundsJson);
        Assert.Equal(source.Window.OrderedTabIds, window.OrderedTabIds);
        Assert.Equal(source.Window.SelectedTabId, window.SelectedTabId);
        Assert.Equal(source.Window.Layout.Id, window.Layout.Id);
        Assert.Equal(source.Window.Layout.Kind, window.Layout.Kind);
        Assert.Equal(source.Window.Layout.Orientation, window.Layout.Orientation);
        Assert.Equal(source.Window.Layout.PrimaryRatio, window.Layout.PrimaryRatio);
        Assert.Equal(source.Window.Layout.Panes, window.Layout.Panes);
        Assert.Equal(source.OtherWindow.Id, saved.Windows[1].Id);
        Assert.Same(close, coordinator.SaveNowAsync(CancellationToken.None));
        Assert.Single(repository.Writes);
    }

    private sealed class Source : IWorkspaceSessionSource
    {
        internal readonly TabSessionSnapshot Tab = new(Guid.NewGuid(), "spaces", "Existing conversation",
            "{\"ConversationId\":\"acacacac-acac-acac-acac-acacacacacac\",\"extensionState\":{\"kept\":true}}",
            "{\"back\":[\"9to1://spaces\"]}", 248, Guid.NewGuid(), true, true,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1));
        internal readonly TabSessionSnapshot SecondTab;
        internal readonly TabGroupSnapshot Group;
        internal readonly WorkspaceWindowSnapshot Window, OtherWindow;
        internal Source()
        {
            SecondTab = Tab with { Id = Guid.NewGuid(), Title = "Second conversation" };
            Group = new(Tab.GroupId!.Value, "Research", true, [Tab.Id, SecondTab.Id]);
            Window = new(Guid.NewGuid(), WorkspaceWindowKind.Main,
                new(Guid.NewGuid(), WorkspaceLayoutKind.Split, SplitOrientation.Horizontal, 0.65,
                    [new(Guid.NewGuid(), Tab.Id, 0), new(Guid.NewGuid(), SecondTab.Id, 1)]),
                [Tab.Id, SecondTab.Id], Tab.Id, "{\"X\":18,\"Y\":27,\"Width\":1280,\"Height\":900}", DateTimeOffset.UnixEpoch);
            OtherWindow = Window with { Id = Guid.NewGuid(), Kind = WorkspaceWindowKind.Normal };
        }
        public bool IsDisposed => false;
        public IReadOnlyList<TabSessionSnapshot> CreateTabSnapshots() => [Tab, SecondTab];
        public IReadOnlyList<TabGroupSnapshot> CreateGroupSnapshots() => [Group];
        public IReadOnlyList<WorkspaceWindowSnapshot> CreateWindowSnapshots(WorkspaceWindowKind kind) => [Window, OtherWindow];
    }

    private sealed class Repository : IWorkspaceSessionRepository
    {
        internal readonly List<WorkspaceSessionSnapshot> Writes = [];
        public Task<WorkspaceSessionSnapshot?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<WorkspaceSessionSnapshot?>(null);
        public Task SaveAsync(WorkspaceSessionSnapshot snapshot, CancellationToken cancellationToken)
        { Writes.Add(snapshot); return Task.CompletedTask; }
    }
}
