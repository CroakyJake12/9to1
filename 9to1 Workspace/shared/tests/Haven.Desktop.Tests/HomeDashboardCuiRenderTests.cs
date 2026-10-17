using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Home;

namespace Haven.Desktop.Tests;

public sealed class HomeDashboardCuiRenderTests
{
    [AvaloniaTheory]
    [InlineData(1280, 900, CuiAppearance.SuperBright)]
    [InlineData(1280, 900, CuiAppearance.Bright)]
    [InlineData(1280, 900, CuiAppearance.Dark)]
    [InlineData(1280, 900, CuiAppearance.SuperDark)]
    [InlineData(390, 844, CuiAppearance.SuperBright)]
    [InlineData(390, 844, CuiAppearance.Bright)]
    [InlineData(390, 844, CuiAppearance.Dark)]
    [InlineData(390, 844, CuiAppearance.SuperDark)]
    public async Task Actual_dashboard_loads_authored_Cui_and_keeps_existing_edit_intent(int width, int height, CuiAppearance appearance)
    {
        var previous = CuiThemeScopeApplier.DetectAppearance();
        var previousTheme = CuiSurfacePaletteCatalog.ActiveTheme;
        CuiThemeScopeApplier.ApplyGlobalTheme(CuiTheme.Professional, appearanceOverride: appearance);
        using var bus = new HavenEventBus();
        // The configured production page reads an explicit test repository. This verifies
        // rendering/intent and data projection, independently from device/provider acceptance.
        using var page = CreatePage(bus, new SnapshotRepository());
        Assert.IsAssignableFrom<IActivatablePage>(page);
        var window = new Window { Content = page, Width = width, Height = height };
        try
        {
            window.Show();
            await page.ActivateAsync(CancellationToken.None);
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));
            window.UpdateLayout();
            var root = Assert.Single(page.GetLogicalDescendants().OfType<CuiContainerControl>(),
                item => item.Name == "dashboard-root");
            Assert.True(root.Bounds.Width > 0 && root.Bounds.Height > 0);
            Assert.True(root.Bounds.Width <= width);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), item =>
                item.Name == "dashboard-welcome" && item.Text?.StartsWith("Good ", StringComparison.Ordinal) == true);
            var edit = Assert.Single(page.GetVisualDescendants().OfType<Button>(), item => item.Name == "dashboard-edit-with-ai");
            var requested = 0;
            page.EditWithHavenRequested += (_, _) => requested++;
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, requested);
            Assert.True(edit.Bounds.Width > 0);
            Assert.True(edit.Bounds.Width <= width);

            foreach (var id in new[] { "dashboard-add-widget", "dashboard-refresh-widgets",
                         "dashboard-undo-layout", "dashboard-redo-layout", "dashboard-arrange-widgets" })
            {
                var control = Assert.Single(page.GetVisualDescendants().OfType<Button>(), item => item.Name == id);
                var origin = control.TranslatePoint(default, page);
                Assert.NotNull(origin);
                Assert.True(control.Bounds.Width > 0);
                Assert.InRange(origin.Value.X, 0, width);
                Assert.True(origin.Value.X + control.Bounds.Width <= width + 1,
                    $"{id} is outside the {width}px dashboard.");
            }
            var arrange = page.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "dashboard-arrange-widgets");
            arrange.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Done arranging", arrange.Content);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), item =>
                item.Name == "dashboard-widget-status" && item.Text?.Contains("live widgets", StringComparison.Ordinal) == true);
        }
        finally { window.Close(); CuiThemeScopeApplier.ApplyGlobalTheme(previousTheme, appearanceOverride: previous); }
    }

    [AvaloniaFact]
    public async Task Reopening_during_cancelled_refresh_reads_again_and_rejects_the_old_result()
    {
        using var bus = new HavenEventBus();
        var source = new SnapshotRepository(blockFirstRead: true);
        using var page = CreatePage(bus, source);
        var window = new Window { Content = page, Width = 1280, Height = 900 };
        try
        {
            window.Show();
            var firstActivation = page.ActivateAsync(CancellationToken.None);
            Assert.Equal(1, source.Reads);
            page.Deactivate();
            var reopened = page.ActivateAsync(CancellationToken.None);
            source.FirstRead.SetResult(Snapshot(91));
            await Task.WhenAll(firstActivation, reopened);
            window.UpdateLayout();

            Assert.Equal(2, source.Reads);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), item => item.Text == "91");
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), item =>
                item.Name == "dashboard-widget-status" && item.Text?.Contains("live widgets", StringComparison.Ordinal) == true);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Close_waits_for_the_actual_held_snapshot_before_detaching_the_dashboard()
    {
        using var bus = new HavenEventBus();
        var source = new SnapshotRepository(blockFirstRead: true);
        var page = CreatePage(bus, source);
        var window = new Window { Content = page, Width = 1000, Height = 800 };
        window.Show();
        try
        {
            var activation = page.ActivateAsync(CancellationToken.None);
            Assert.Equal(1, source.Reads);
            var close = page.CloseAndDrainAsync();
            Assert.Same(close, page.OriginalClose);
            Assert.Same(close, page.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
            Assert.NotNull(page.Content);
            source.FirstRead.TrySetResult(Snapshot());
            // The existing original-work owner retains cancellation evidence. A terminal
            // cancellation is not permission to detach before the actual source has joined.
            _ = await Record.ExceptionAsync(() => activation);
            _ = await Record.ExceptionAsync(() => close);
            Assert.True(activation.IsCompleted);
            Assert.Null(page.Content);
        }
        finally { source.FirstRead.TrySetResult(Snapshot()); window.Close(); page.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Open_editor_veto_preserves_draft_and_closing_joins_its_admitted_save()
    {
        using var bus = new HavenEventBus();
        var settings = new TestSettings();
        var page = new NewDashboardPage(bus, null!, null!, null!, null!, settings, new SnapshotRepository(), new TestLayout(), []);
        var window = new Window { Content = page, Width = 1000, Height = 800 };
        window.Show();
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "dashboard-add-page")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(await page.TryPrepareToCloseAsync(CancellationToken.None));
            Assert.NotNull(page.Content);
            var field = typeof(NewDashboardPage).GetField("_homeEditors", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var editor = Assert.Single((IEnumerable<Flyout>)field.GetValue(page)!);
            var control = Assert.IsAssignableFrom<Control>(editor.Content);
            var title = control.GetLogicalDescendants().OfType<TextBox>().First();
            title.Text = "Retain my draft";
            Assert.False(await page.TryPrepareToCloseAsync(CancellationToken.None));
            Assert.Equal("Retain my draft", title.Text);
            settings.HoldWrites = true;
            control.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Create page"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await settings.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var close = page.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            Assert.NotNull(page.Content);
            settings.WriteRelease.TrySetResult();
            _ = await Record.ExceptionAsync(() => close);
            Assert.True(settings.WriteAcknowledged);
            Assert.Null(page.Content);
        }
        finally { settings.WriteRelease.TrySetResult(); window.Close(); page.Dispose(); }
    }

    private static NewDashboardPage CreatePage(HavenEventBus bus, IDashboardRepository source) =>
        new(bus, null!, null!, null!, null!, new TestSettings(), source, new TestLayout(), []);

    private static DashboardSnapshot Snapshot(int tasksDueToday = 3) =>
        new(DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, tasksDueToday, 0, 0, 0, 0, 0, TimeSpan.Zero, [], []);

    private sealed class SnapshotRepository(bool blockFirstRead = false) : IDashboardRepository
    {
        public int Reads { get; private set; }
        public TaskCompletionSource<DashboardSnapshot> FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DashboardSnapshot> GetSnapshotAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            Reads++;
            // Deliberately ignore cancellation for the first read to exercise stale-result rejection.
            return blockFirstRead && Reads == 1 ? FirstRead.Task : Task.FromResult(Snapshot());
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
        public bool HoldWrites;
        public bool WriteAcknowledged;
        public TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WriteRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class =>
            Task.FromResult(_values.GetValueOrDefault(key) as T);
        public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            if (HoldWrites) { WriteEntered.TrySetResult(); await WriteRelease.Task; WriteAcknowledged = true; }
            _values[key] = value;
        }
        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
