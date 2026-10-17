using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Apps.Browse;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class UnifiedWorkspaceSessionTests
{
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
