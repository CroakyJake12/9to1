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
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class HomeDashboardCuiRenderTests
{
    [AvaloniaTheory]
    [InlineData(340)]
    [InlineData(760)]
    [InlineData(1280)]
    public async Task Device_model_cards_reflow_and_open_the_same_verified_review_without_installing(int width)
    {
        using var bus = new HavenEventBus();
        using var page = CreatePage(bus, new SnapshotRepository());
        var catalogue = new DeviceCatalogue();
        page.EnableDeviceModelRecommendations(new(new DeviceProbe(), catalogue,
            _ => ValueTask.FromResult<AuthenticatedResourceActor?>(DeviceActor), () => true));
        var window = new Window { Width = width, Height = 900, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(CancellationToken.None);
            window.UpdateLayout();
            var cards = page.GetVisualDescendants().OfType<CuiContainerControl>().Where(control => control.Name?.StartsWith("dashboard-model-card", StringComparison.Ordinal) == true && control.Name != "dashboard-model-cards").ToArray();
            Assert.Equal(3, cards.Length);
            foreach (var card in cards)
            {
                var origin = card.TranslatePoint(default, page)!.Value;
                Assert.InRange(origin.X, 0, width);
                Assert.True(origin.X + card.Bounds.Width <= width + 1);
                Assert.True(card.Bounds.Width > 0);
                Assert.NotNull(card.Background);
            }
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), control => control.Text?.Contains("Fixture GPU", StringComparison.Ordinal) == true);
            var review = cards[0].GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Review & install"));
            review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var loader = (CuiControlLoader)typeof(NewDashboardPage).GetField("_dashboardLoader", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(page)!;
            await loader.WhenActionsIdleAsync();
            window.UpdateLayout();
            var files = page.GetVisualDescendants().OfType<ListBox>().Single(control => control.Name == "hf-files");
            Assert.IsType<HuggingFaceModelFile>(files.SelectedItem);
            Assert.Equal(0, catalogue.InstallCalls);
            Assert.True(page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "hf-install").IsEnabled);
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task Local_only_recommendations_observe_hardware_without_contacting_the_catalogue()
    {
        var catalogue = new DeviceCatalogue();
        var source = new HomeDashboardModelSource(new DeviceProbe(), catalogue,
            _ => ValueTask.FromResult<AuthenticatedResourceActor?>(DeviceActor), () => false);
        var snapshot = await source.ReadAsync(CancellationToken.None);
        Assert.Empty(snapshot.Models);
        Assert.Equal(0, catalogue.Requests);
        Assert.Contains("Local-only", snapshot.Status);
        Assert.Equal(32 * LocalModelMemoryEstimator.GiB, snapshot.Hardware.TotalRamBytes);
        Assert.Null(await source.Profile(CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recommendation_source_rejects_late_results_after_profile_change_or_cancellation(bool cancelled)
    {
        var actor = DeviceActor;
        using var cancellation = new CancellationTokenSource();
        var catalogue = new DeviceCatalogue { HoldDetails = true };
        var source = new HomeDashboardModelSource(new DeviceProbe(), catalogue,
            _ => ValueTask.FromResult<AuthenticatedResourceActor?>(actor), () => true);
        var loading = source.ReadAsync(cancellation.Token);
        await catalogue.DetailsEntered.Task;
        if (cancelled) cancellation.Cancel();
        else actor = actor with { AuthenticationRevision = "new-authentication" };
        catalogue.ReleaseDetails.TrySetResult();
        var error = await Record.ExceptionAsync(() => loading);
        if (cancelled) Assert.IsAssignableFrom<OperationCanceledException>(error);
        else Assert.IsType<UnauthorizedAccessException>(error);
        Assert.Equal(0, catalogue.InstallCalls);
    }

    [Fact]
    public void Memory_fit_retains_unknown_exhaustion_headroom_and_individual_GPU_limits()
    {
        const long gb = LocalModelMemoryEstimator.GiB;
        var hardware = DeviceProbe.Hardware;
        Assert.Equal(LocalModelMemoryFit.Gpu, LocalModelMemoryEstimator.Estimate(gb, hardware).Kind);
        Assert.Equal(LocalModelMemoryFit.Unknown, LocalModelMemoryEstimator.Estimate(gb, hardware with { AvailableRamBytes = null }).Kind);
        Assert.Equal(LocalModelMemoryFit.TooLarge, LocalModelMemoryEstimator.Estimate(gb, hardware with { AvailableRamBytes = 0 }).Kind);
        Assert.Equal(LocalModelMemoryFit.TooLarge, LocalModelMemoryEstimator.Estimate(gb, hardware with { AvailableRamBytes = 3 * gb }).Kind);
        Assert.Equal(LocalModelMemoryFit.Cpu, LocalModelMemoryEstimator.Estimate(5 * gb,
            hardware with { Gpus = [new("A", 8 * gb, 8 * gb), new("B", 8 * gb, 8 * gb)] }).Kind);
        Assert.Equal(LocalModelMemoryFit.Unknown, LocalModelMemoryEstimator.Estimate(long.MaxValue, hardware).Kind);
        var details = new HuggingFaceModelDetails("fixture/model", new string('b', 40), "MIT", false,
            [new("mmproj.gguf", gb, new string('a', 64)), new("model-00001-of-00002.gguf", gb, new string('a', 64))]);
        Assert.Empty(LocalModelMemoryEstimator.Rank(hardware, [(new("fixture/model", 10, 0, false), details)]));
    }

    private static readonly AuthenticatedResourceActor DeviceActor = new("fixture-actor", "fixture-profile", null, null, "fixture-authentication");
    private sealed class DeviceProbe : ILocalModelHardwareProbe
    {
        internal static LocalModelHardware Hardware => new(DateTimeOffset.UtcNow, "X64", 8,
            32 * LocalModelMemoryEstimator.GiB, 20 * LocalModelMemoryEstimator.GiB,
            [new("Fixture GPU", 8 * LocalModelMemoryEstimator.GiB, 8 * LocalModelMemoryEstimator.GiB)]);
        public Task<LocalModelHardware> ReadAsync(CancellationToken token) => Task.FromResult(Hardware);
    }
    private sealed class DeviceCatalogue : IHuggingFaceModelService
    {
        internal int Requests, InstallCalls;
        internal bool HoldDetails;
        internal TaskCompletionSource DetailsEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseDetails = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<HuggingFaceModelSummary>> SearchAsync(string profile, string query, CancellationToken token)
        {
            Requests++;
            return Task.FromResult<IReadOnlyList<HuggingFaceModelSummary>>(Enumerable.Range(0, 3).Select(index =>
                new HuggingFaceModelSummary("fixture/model-" + index, 100 - index, 0, false)).ToArray());
        }
        public async Task<HuggingFaceModelDetails> GetDetailsAsync(string profile, string id, CancellationToken token)
        {
            Requests++; DetailsEntered.TrySetResult();
            if (HoldDetails) await ReleaseDetails.Task; // Deliberately late, uncancellable provider reply.
            return new(id, new string('b', 40), "MIT", false, [new("model.Q4_K_M.gguf", LocalModelMemoryEstimator.GiB, new string('a', 64))]);
        }
        public Task<HuggingFaceInstallResult> InstallAsync(string profile, HuggingFaceModelDetails model, HuggingFaceModelFile file,
            IProgress<HuggingFaceInstallProgress>? progress, CancellationToken token)
        { InstallCalls++; throw new InvalidOperationException("Review must not install a model."); }
        public Task<bool> HasTokenAsync(string profile, CancellationToken token) => Task.FromResult(false);
        public Task SaveTokenAsync(string profile, string value, CancellationToken token) => throw new NotSupportedException();
        public Task RemoveTokenAsync(string profile, CancellationToken token) => throw new NotSupportedException();
    }

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
            Assert.False(edit.IsEffectivelyVisible);
            var arrange = page.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "dashboard-arrange-widgets");
            Assert.Equal("Customise", arrange.Content);
            arrange.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Assert.True(edit.IsEffectivelyVisible);
            Assert.Equal("Done", arrange.Content);
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
            arrange.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Assert.Equal("Customise", arrange.Content);
            Assert.False(edit.IsEffectivelyVisible);
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
            page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "dashboard-arrange-widgets")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
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
