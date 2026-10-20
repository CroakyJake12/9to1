using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Home;

namespace Haven.Desktop.Tests;

/// <summary>
/// Focused tests for the source-backed Home dashboard briefing sections (TODAY agenda and RECENT
/// WORK). These exercise the typed binding projection, the authority-checked target routing and
/// the truthful loading/empty/stale/error states. Tests are authored here; they are NOT run by the
/// implementation worker (root allocates the build/test slot).
/// </summary>
public sealed class HomeDashboardBriefingCuiTests
{
    // ---------- Typed projection ----------

    [Fact]
    public void Fresh_snapshot_projects_openable_rows_with_provenance()
    {
        var taskId = Guid.NewGuid().ToString("D");
        var chatId = Guid.NewGuid().ToString("D");
        var agenda = new DashboardAgendaItem(taskId, "task", "Ship report", "Notes", TodayAt(9), false, "plan");
        var recent = new DashboardWorkItem(chatId, "chat", "Design review", "Chat conversation", Now, "chat", "chat");
        var state = DashboardBriefingViewState.FromSnapshot(Snapshot(agenda, recent), stale: false, error: null);

        Assert.Equal(DashboardBriefingPhase.Ready, state.Phase);
        Assert.Single(state.Agenda);
        Assert.Single(state.RecentWork);
        Assert.True(state.Agenda[0].CanOpen);
        Assert.Equal("Planner task", state.Agenda[0].Provenance);
        Assert.True(state.RecentWork[0].CanOpen);
        Assert.Equal("Chat conversation", state.RecentWork[0].Provenance);
        Assert.Equal(new DashboardBriefingTarget("task", taskId, "plan"), state.Agenda[0].Target);
        Assert.Equal(new DashboardBriefingTarget("chat", chatId, "chat"), state.RecentWork[0].Target);
    }

    [Fact]
    public void Stale_snapshot_marks_rows_stale_and_non_actionable()
    {
        var agenda = new DashboardAgendaItem(Guid.NewGuid().ToString("D"), "task", "Ship report", "", TodayAt(9), false, "plan");
        var state = DashboardBriefingViewState.FromSnapshot(Snapshot(agenda, null), stale: true, error: null);

        Assert.Equal(DashboardBriefingPhase.Stale, state.Phase);
        Assert.Single(state.Agenda);
        Assert.False(state.Agenda[0].CanOpen);
        Assert.True(state.ShowRetry);
    }

    [Fact]
    public void Error_without_prior_snapshot_is_truthful_and_never_fabricates_rows()
    {
        var state = DashboardBriefingViewState.FromSnapshot(null, stale: true, error: "source revoked");

        Assert.Equal(DashboardBriefingPhase.Error, state.Phase);
        Assert.Empty(state.Agenda);
        Assert.Empty(state.RecentWork);
        Assert.False(state.HasOpenable);
        Assert.True(state.ShowRetry);
    }

    [Fact]
    public void Empty_fresh_snapshot_surfaces_truthful_empty_states()
    {
        var state = DashboardBriefingViewState.FromSnapshot(Snapshot(null, null), stale: false, error: null);

        Assert.Equal(DashboardBriefingPhase.Ready, state.Phase);
        Assert.True(state.NoAgenda);
        Assert.True(state.NoRecent);
        Assert.False(state.HasAgenda);
        Assert.False(state.HasRecent);
        Assert.Contains("Nothing is scheduled", state.AgendaStatus);
    }

    // ---------- Bindings dispatch ----------

    [Fact]
    public async Task Bindings_dispatch_typed_target_and_refresh_commands_only()
    {
        var state = DashboardBriefingViewState.FromSnapshot(Snapshot(null, null), stale: false, error: null);
        var targets = new List<DashboardBriefingTarget>();
        var refreshes = 0;
        var bindings = new NewDashboardPageBriefingBindings(
            () => state,
            (command, parameter, _) =>
            {
                if (command == NewDashboardPageBriefingBindings.OpenCommand && parameter is DashboardBriefingTarget t) targets.Add(t);
                if (command == NewDashboardPageBriefingBindings.RefreshCommand) refreshes++;
                return default;
            });

        var target = new DashboardBriefingTarget("task", Guid.NewGuid().ToString("D"), "plan");
        await bindings.DispatchAsync(NewDashboardPageBriefingBindings.OpenCommand, target);
        await bindings.DispatchAsync(NewDashboardPageBriefingBindings.RefreshCommand, null);

        Assert.Single(targets);
        Assert.Equal(1, refreshes);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bindings.DispatchAsync(NewDashboardPageBriefingBindings.OpenCommand, "not-a-target").AsTask());
    }

    [Fact]
    public void Bindings_availability_reflects_current_phase()
    {
        var stale = DashboardBriefingViewState.FromSnapshot(Snapshot(null, null), stale: true, error: null);
        var bindings = new NewDashboardPageBriefingBindings(() => stale, (_, _, _) => default);
        Assert.Equal(true, bindings.IsActionAvailable(NewDashboardPageBriefingBindings.RefreshCommand));
        Assert.Equal(false, bindings.IsActionAvailable(NewDashboardPageBriefingBindings.OpenCommand));
        Assert.Null(bindings.IsActionAvailable("unrelated.command"));
    }

    // ---------- Authority-checked page routing ----------

    [AvaloniaFact]
    public async Task Valid_target_routes_to_navigator_refusal_never_reaches_it()
    {
        using var bus = new HavenEventBus();
        var chatId = Guid.NewGuid().ToString("D");
        var source = new BriefingRepository(chatId, Guid.NewGuid().ToString("D"));
        using var page = CreatePage(bus, source);
        var navigated = new List<DashboardBriefingTarget>();
        page.BriefingNavigator = target => { navigated.Add(target); return true; };

        var window = new Window { Content = page, Width = 1000, Height = 800 };
        try
        {
            window.Show();
            await page.ActivateAsync(CancellationToken.None);
            window.UpdateLayout();

            var valid = new DashboardBriefingTarget("chat", chatId, "chat");
            Assert.True(page.TryNavigateBriefing(valid));
            Assert.Equal(valid, Assert.Single(navigated));

            var unknown = new DashboardBriefingTarget("chat", Guid.NewGuid().ToString("D"), "chat");
            Assert.False(page.TryNavigateBriefing(unknown));
            Assert.Single(navigated); // the unknown target never reached the host
            Assert.False(string.IsNullOrEmpty(page.LastBriefingRefusal));

            var staleKind = new DashboardBriefingTarget("call", Guid.NewGuid().ToString("D"), "call");
            Assert.False(page.TryNavigateBriefing(staleKind));
            Assert.Single(navigated);
        }
        finally { window.Close(); page.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Deactivated_page_rejects_late_resolved_source_so_it_cannot_update_content()
    {
        using var bus = new HavenEventBus();
        var chatId = Guid.NewGuid().ToString("D");
        var source = new BlockingBriefingRepository(chatId, Guid.NewGuid().ToString("D"));
        using var page = CreatePage(bus, source);
        var window = new Window { Content = page, Width = 1000, Height = 800 };
        window.Show();
        try
        {
            var activation = page.ActivateAsync(CancellationToken.None);
            Assert.Equal(1, source.Reads);
            page.Deactivate();
            source.FirstRead.SetResult(source.LateSnapshot());
            _ = await Record.ExceptionAsync(() => activation);
            window.UpdateLayout();

            // The retired activation's late source resolution must not surface rows as current.
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), item => item.Text == "Ship report");
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), item => item.Text == "Design review");
        }
        finally { window.Close(); page.Dispose(); }
    }

    // ---------- Render surface ----------

    [AvaloniaTheory]
    [InlineData(1280, 900)]
    [InlineData(390, 844)]
    public async Task Dashboard_renders_real_briefing_rows_and_truthful_empty_state(int width, int height)
    {
        using var bus = new HavenEventBus();
        using var populated = CreatePage(bus, new BriefingRepository(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D")));
        using var populatedWindow = new Window { Content = populated, Width = width, Height = height };
        populatedWindow.Show();
        await populated.ActivateAsync(CancellationToken.None);
        populatedWindow.Measure(new Size(width, height));
        populatedWindow.Arrange(new Rect(0, 0, width, height));
        populatedWindow.UpdateLayout();

        Assert.Contains(populated.GetVisualDescendants().OfType<TextBlock>(), item =>
            item.Text == "Ship report" || item.Text == "Design review");
        Assert.Contains(populated.GetVisualDescendants().OfType<TextBlock>(), item =>
            item.Text?.Contains("Planner task", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(populated.GetVisualDescendants().OfType<TextBlock>(), item =>
            item.Name == "dashboard-agenda-empty");

        populatedWindow.Close();

        using var empty = CreatePage(bus, new BriefingRepository(null, null));
        using var emptyWindow = new Window { Content = empty, Width = width, Height = height };
        emptyWindow.Show();
        await empty.ActivateAsync(CancellationToken.None);
        emptyWindow.UpdateLayout();

        Assert.Contains(empty.GetVisualDescendants().OfType<TextBlock>(), item =>
            item.Name == "dashboard-agenda-empty" && item.Text?.Contains("Nothing is scheduled", StringComparison.Ordinal) == true);
        Assert.Contains(empty.GetVisualDescendants().OfType<TextBlock>(), item =>
            item.Name == "dashboard-recent-empty");
        emptyWindow.Close();
    }

    // ---------- Helpers ----------

    private static NewDashboardPage CreatePage(HavenEventBus bus, IDashboardRepository source) =>
        new(bus, null!, null!, null!, null!, new TestSettings(), source, new TestLayout(), []);

    private static DashboardSnapshot Snapshot(DashboardAgendaItem? agenda, DashboardWorkItem? recent) =>
        new(DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero,
            agenda is null ? [] : new[] { agenda },
            recent is null ? [] : new[] { recent });

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;
    private static DateTimeOffset TodayAt(int hour) => new(DateTimeOffset.UtcNow.Date.AddHours(hour), TimeSpan.Zero);

    private sealed class BriefingRepository(string? chatId, string? taskId) : IDashboardRepository
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            var agenda = taskId is null ? [] : new[]
            {
                new DashboardAgendaItem(taskId, "task", "Ship report", "Notes", TodayAt(9), false, "plan")
            };
            var recent = chatId is null ? [] : new[]
            {
                new DashboardWorkItem(chatId, "chat", "Design review", "Chat conversation", Now, "chat", "chat")
            };
            return Task.FromResult(new DashboardSnapshot(now, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, agenda, recent));
        }
    }

    private sealed class BlockingBriefingRepository(string chatId, string taskId) : IDashboardRepository
    {
        public int Reads { get; private set; }
        public TaskCompletionSource<DashboardSnapshot> FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<DashboardSnapshot> GetSnapshotAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            Reads++;
            return Reads == 1 ? FirstRead.Task : Task.FromResult(LateSnapshot());
        }

        public DashboardSnapshot LateSnapshot() => new(DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero,
            new[] { new DashboardAgendaItem(taskId, "task", "Ship report", "Notes", TodayAt(9), false, "plan") },
            new[] { new DashboardWorkItem(chatId, "chat", "Design review", "Chat conversation", Now, "chat", "chat") });
    }

    private sealed class TestLayout : IDashboardLayoutRepository
    {
        public Task<IReadOnlyList<DashboardTileLayout>> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DashboardTileLayout>>([]);
        public Task SaveAsync(IReadOnlyList<DashboardTileLayout> layout, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestSettings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class =>
            Task.FromResult(_values.GetValueOrDefault(key) as T);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
