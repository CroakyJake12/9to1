using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
/// WORK). These exercise the typed binding projection, the authority-checked live-source navigation,
/// the truthful loading/empty/stale/error/revoked states and the held-original close settlement.
/// Tests are authored here; they are NOT run by the implementation worker (root owns the build/test
/// slot).
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
        Assert.False(state.HasRefusal);
    }

    [Fact]
    public void Stale_snapshot_marks_rows_stale_and_non_actionable_and_stale_explanation_wins()
    {
        var agenda = new DashboardAgendaItem(Guid.NewGuid().ToString("D"), "task", "Ship report", "", TodayAt(9), false, "plan");
        var state = DashboardBriefingViewState.FromSnapshot(Snapshot(agenda, null), stale: true, error: null);

        Assert.Equal(DashboardBriefingPhase.Stale, state.Phase);
        Assert.Single(state.Agenda);
        Assert.False(state.Agenda[0].CanOpen);
        Assert.True(state.ShowRetry);
        // Stale explanation must win over the populated count; stale rows are never shown as current.
        Assert.Contains("stale", state.AgendaStatus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("items in today's agenda", state.AgendaStatus);
    }

    [Fact]
    public void Error_without_source_is_truthful_and_purges_all_rows()
    {
        var state = DashboardBriefingViewState.FromSnapshot(null, stale: true, error: "source revoked");

        Assert.Equal(DashboardBriefingPhase.Error, state.Phase);
        Assert.Empty(state.Agenda);
        Assert.Empty(state.RecentWork);
        Assert.False(state.HasOpenable);
        Assert.False(state.HasAgenda);
        Assert.False(state.HasRecent);
        Assert.True(state.ShowRetry);
    }

    [Fact]
    public void Rvoked_state_purges_content_and_surfaces_distinct_explanation()
    {
        var state = DashboardBriefingViewState.Revoked("This area is no longer authorized.");

        Assert.Equal(DashboardBriefingPhase.Revoked, state.Phase);
        Assert.Empty(state.Agenda);
        Assert.Empty(state.RecentWork);
        Assert.False(state.HasOpenable);
        Assert.Contains("no longer authorized", state.AgendaStatus, StringComparison.OrdinalIgnoreCase);
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

    // ---------- Authority-checked live-source navigation ----------

    [AvaloniaFact]
    public async Task Valid_target_routes_after_live_revalidation_unknown_target_is_refused_and_visible()
    {
        using var bus = new HavenEventBus();
        var chatId = Guid.NewGuid().ToString("D");
        var taskId = Guid.NewGuid().ToString("D");
        var repo = new ControlledRepository(() => Snapshot(TaskAt(taskId), Chat(chatId)));
        using var page = CreatePage(bus, repo);
        var navigated = new List<DashboardBriefingTarget>();
        page.BriefingNavigator = target => { navigated.Add(target); return true; };

        var window = new Window { Content = page, Width = 1000, Height = 800 };
        try
        {
            window.Show();
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            window.UpdateLayout();

            var valid = new DashboardBriefingTarget("chat", chatId, "chat");
            Assert.True(await page.TryNavigateBriefingAsync(valid));
            Assert.Equal(valid, Assert.Single(navigated));
            Assert.Equal(string.Empty, page.LastBriefingRefusal);

            var unknown = new DashboardBriefingTarget("chat", Guid.NewGuid().ToString("D"), "chat");
            Assert.False(await page.TryNavigateBriefingAsync(unknown));
            Assert.Single(navigated); // the unknown target never reached the host
            Assert.False(string.IsNullOrEmpty(page.LastBriefingRefusal));

            // The refusal is surfaced in the authored CUI, not hidden.
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), item =>
                item.Name == "dashboard-briefing-refusal" && item.Text?.Length > 0);
        }
        finally { window.Close(); page.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Held_source_navigation_joins_original_and_revoking_between_reads_forbids_navigation()
    {
        using var bus = new HavenEventBus();
        var chatId = Guid.NewGuid().ToString("D");
        var repo = new ControlledRepository(() => Snapshot(null, Chat(chatId)));
        using var page = CreatePage(bus, repo);
        var navigated = 0;
        page.BriefingNavigator = _ => { navigated++; return true; };

        var window = new Window { Content = page, Width = 1000, Height = 800 };
        try
        {
            window.Show();
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            window.UpdateLayout();

            var valid = new DashboardBriefingTarget("chat", chatId, "chat");

            // Hold the next current-source read (the navigation's revalidation read).
            using var hold = new TaskCompletionSource<DashboardSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            repo.HoldRead = hold;
            var navigation = page.TryNavigateBriefingAsync(valid);
            Assert.False(navigation.IsCompleted);

            // Revoke/delete the row before the held read resolves.
            hold.SetResult(Snapshot(null, null));
            Assert.False(await navigation);
            Assert.Equal(0, navigated);
            Assert.False(string.IsNullOrEmpty(page.LastBriefingRefusal));
        }
        finally { window.Close(); page.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Held_source_navigation_waits_for_current_source_then_navigates_exactly_once()
    {
        using var bus = new HavenEventBus();
        var chatId = Guid.NewGuid().ToString("D");
        var repo = new ControlledRepository(() => Snapshot(null, Chat(chatId)));
        using var page = CreatePage(bus, repo);
        var navigated = 0;
        page.BriefingNavigator = _ => { navigated++; return true; };

        var window = new Window { Content = page, Width = 1000, Height = 800 };
        try
        {
            window.Show();
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            window.UpdateLayout();

            var valid = new DashboardBriefingTarget("chat", chatId, "chat");
            using var hold = new TaskCompletionSource<DashboardSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            repo.HoldRead = hold;
            var navigation = page.TryNavigateBriefingAsync(valid);
            Assert.False(navigation.IsCompleted);

            hold.SetResult(Snapshot(null, Chat(chatId)));
            Assert.True(await navigation);
            Assert.Equal(1, navigated);
            Assert.Equal(string.Empty, page.LastBriefingRefusal);
        }
        finally { window.Close(); page.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Inactive_page_refuses_without_any_source_read()
    {
        using var bus = new HavenEventBus();
        var chatId = Guid.NewGuid().ToString("D");
        var repo = new ControlledRepository(() => Snapshot(null, Chat(chatId)));
        using var page = CreatePage(bus, repo);
        var navigated = 0;
        page.BriefingNavigator = _ => { navigated++; return true; };

        // Never activated: the page is inactive, so no source read and no navigation.
        var valid = new DashboardBriefingTarget("chat", chatId, "chat");
        Assert.False(await page.TryNavigateBriefingAsync(valid));
        Assert.Equal(0, navigated);
        Assert.Equal(0, repo.Reads);
        Assert.False(string.IsNullOrEmpty(page.LastBriefingRefusal));
    }

    [AvaloniaFact]
    public async Task Close_joins_a_held_briefing_navigation_before_detaching()
    {
        using var bus = new HavenEventBus();
        var chatId = Guid.NewGuid().ToString("D");
        var repo = new ControlledRepository(() => Snapshot(null, Chat(chatId)));
        using var page = CreatePage(bus, repo);
        var navigated = 0;
        page.BriefingNavigator = _ => { navigated++; return true; };

        var window = new Window { Content = page, Width = 1000, Height = 800 };
        window.Show();
        try
        {
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            var valid = new DashboardBriefingTarget("chat", chatId, "chat");
            using var hold = new TaskCompletionSource<DashboardSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            repo.HoldRead = hold;
            var navigation = page.TryNavigateBriefingAsync(valid);

            var close = page.CloseAndDrainAsync();
            Assert.False(close.IsCompleted); // close must await the held navigation original
            hold.SetResult(Snapshot(null, null)); // revoke: navigation refuses, then close drains
            _ = await Record.ExceptionAsync(() => navigation);
            _ = await Record.ExceptionAsync(() => close);
            Assert.True(close.IsCompleted);
            Assert.Equal(0, navigated);
        }
        finally { window.Close(); page.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Deactivated_page_rejects_late_resolved_source_so_it_cannot_update_content()
    {
        using var bus = new HavenEventBus();
        var chatId = Guid.NewGuid().ToString("D");
        var taskId = Guid.NewGuid().ToString("D");
        var repo = new ControlledRepository(() => Snapshot(TaskAt(taskId), Chat(chatId)));
        using var page = CreatePage(bus, repo);
        var window = new Window { Content = page, Width = 1000, Height = 800 };
        window.Show();
        try
        {
            using var hold = new TaskCompletionSource<DashboardSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            repo.HoldRead = hold;
            var activation = page.ActivateAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, repo.Reads);
            page.Deactivate();
            hold.SetResult(Snapshot(TaskAt(taskId), Chat(chatId)));
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
        using var populated = CreatePage(bus, new ControlledRepository(() =>
            Snapshot(TaskAt(Guid.NewGuid().ToString("D")), Chat(Guid.NewGuid().ToString("D")))));
        var populatedWindow = new Window { Content = populated, Width = width, Height = height };
        try
        {
            populatedWindow.Show();
            await populated.ActivateAsync(TestContext.Current.CancellationToken);
            populatedWindow.Measure(new Size(width, height));
            populatedWindow.Arrange(new Rect(0, 0, width, height));
            populatedWindow.UpdateLayout();

            Assert.Contains(populated.GetVisualDescendants().OfType<TextBlock>(), item =>
                item.Text == "Ship report" || item.Text == "Design review");
            Assert.Contains(populated.GetVisualDescendants().OfType<TextBlock>(), item =>
                item.Text?.Contains("Planner task", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(populated.GetVisualDescendants().OfType<TextBlock>(), item =>
                item.Name == "dashboard-agenda-empty");
            Assert.DoesNotContain(populated.GetVisualDescendants().OfType<TextBlock>(), item =>
                item.Name == "dashboard-briefing-refusal" && item.Text?.Length > 0);
        }
        finally { populatedWindow.Close(); }

        using var empty = CreatePage(bus, new ControlledRepository(() => Snapshot(null, null)));
        var emptyWindow = new Window { Content = empty, Width = width, Height = height };
        try
        {
            emptyWindow.Show();
            await empty.ActivateAsync(TestContext.Current.CancellationToken);
            emptyWindow.UpdateLayout();

            Assert.Contains(empty.GetVisualDescendants().OfType<TextBlock>(), item =>
                item.Name == "dashboard-agenda-empty" && item.Text?.Contains("Nothing is scheduled", StringComparison.Ordinal) == true);
            Assert.Contains(empty.GetVisualDescendants().OfType<TextBlock>(), item =>
                item.Name == "dashboard-recent-empty");
        }
        finally { emptyWindow.Close(); }
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

    private static DashboardAgendaItem TaskAt(string id) =>
        new(id, "task", "Ship report", "Notes", TodayAt(9), false, "plan");

    private static DashboardWorkItem Chat(string id) =>
        new(id, "chat", "Design review", "Chat conversation", Now, "chat", "chat");

    /// <summary>Controllable real repository adapter: serves a current snapshot and can optionally
    /// hold the next read so tests can exercise held-source revalidation and close joining.</summary>
    private sealed class ControlledRepository(Func<DashboardSnapshot> next) : IDashboardRepository
    {
        public int Reads { get; private set; }
        public TaskCompletionSource<DashboardSnapshot>? HoldRead { get; set; }

        public Task<DashboardSnapshot> GetSnapshotAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            Reads++;
            return HoldRead is { } hold ? hold.Task : Task.FromResult(next());
        }
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
