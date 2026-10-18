using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Spaces.Builder;

namespace HavenOS.Browse.Host.Tests;

/// <summary>Actual retained page/controller/CUI against the production file store.
/// This is headless component evidence, not Home/ACL/navigation/native acceptance.</summary>
public sealed class SpaceDetailsPageTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);

    [AvaloniaFact]
    public async Task Actual_editor_inputs_and_save_button_persist_the_same_record_and_keep_unexposed_configuration()
    {
        using var files = new Files();
        var registry = files.Registry();
        var space = await registry.CreateAsync("Original");
        space = await registry.UpdateAsync(space with { ModelName = "configured-model", Instructions = "Saved instructions" }, space.Revision);
        var page = new SpaceDetailsPage(registry, space.Id, Ready.Instance);
        using var view = new MountedPage(page);
        try
        {
            await page.ActivateAsync().WaitAsync(Bound);
            Input(page.EditorHost, "space-details-name").Text = "Saved native adapter";
            Input(page.EditorHost, "space-details-description").Text = "Actual authored input";
            Assert.True(page.State.HasChanges);
            Click(page.EditorHost, "space-details-save");
            await page.EditorHost.CaptureOriginalActionSettlement(page.EditorShow!).WaitAsync(Bound);
            var saved = (await files.Registry().GetAsync(space.Id))!;
            Assert.Equal(space.Id, page.State.SpaceId);
            Assert.Equal("Saved native adapter", saved.Name);
            Assert.Equal("Actual authored input", saved.Description);
            Assert.Equal("configured-model", saved.ModelName);
            Assert.Equal("Saved instructions", saved.Instructions);
            Assert.False(page.State.HasChanges);
            Assert.False(File.Exists(files.DatabasePath));
        }
        finally { await page.CloseAndDrainAsync().WaitAsync(Bound); }
    }

    [AvaloniaTheory]
    [InlineData("save")]
    [InlineData("discard")]
    [InlineData("cancel")]
    public async Task Actual_three_choice_buttons_preserve_or_resolve_the_draft_and_only_exact_external_commit_retires(string choice)
    {
        using var files = new Files();
        var registry = files.Registry(); var space = await registry.CreateAsync("Original");
        var page = new SpaceDetailsPage(registry, space.Id, Ready.Instance);
        using var view = new MountedPage(page);
        try
        {
            await page.ActivateAsync().WaitAsync(Bound);
            Input(page.EditorHost, "space-details-name").Text = "Unsaved title";
            var prepare = page.PrepareCloseAsync();
            await DecisionReady(page);
            Assert.False(prepare.IsCompleted);
            Click(page.DecisionHost!, "space-details-close-" + choice);
            var lease = await prepare.WaitAsync(Bound);
            Assert.Null(page.OriginalClose);
            Assert.Same(page.EditorHost, page.Content);
            Assert.Equal(CuiSceneAvailabilityState.Ready, page.EditorHost.Availability!.State);
            if (choice == "cancel")
            {
                Assert.Null(lease);
                Assert.Equal("Unsaved title", page.State.Draft.Name);
                Assert.True(page.TryEdit(SpaceDetailsField.Description, "Still editing"));
            }
            else
            {
                Assert.NotNull(lease);
                Assert.False(page.TryEdit(SpaceDetailsField.Description, "Must refuse after accepted preparation"));
                Assert.Equal(choice == "save" ? "Unsaved title" : "Original", page.State.Draft.Name);
                Assert.True(await page.CommitCloseAsync(lease).WaitAsync(Bound));
                Assert.True(page.OriginalClose!.IsCompletedSuccessfully);
            }
            Assert.Equal(choice == "save" ? "Unsaved title" : "Original", (await files.Registry().GetAsync(space.Id))!.Name);
        }
        finally { await page.CloseAndDrainAsync().WaitAsync(Bound); }
    }

    [AvaloniaFact]
    public async Task Held_actual_save_keeps_new_typing_and_refuses_preparation_until_the_actual_operation_settles()
    {
        using var files = new Files();
        var source = new ControlledStore(files.Store()); var registry = new SpaceRegistry(source);
        var space = await registry.CreateAsync("Original");
        var page = new SpaceDetailsPage(registry, space.Id, Ready.Instance);
        using var view = new MountedPage(page);
        Task<bool>? save = null;
        try
        {
            await page.ActivateAsync().WaitAsync(Bound);
            Input(page.EditorHost, "space-details-name").Text = "Captured save";
            source.Hold = true;
            save = page.SaveAsync();
            await source.Entered.Task.WaitAsync(Bound);
            Input(page.EditorHost, "space-details-description").Text = "Typed during real held save";
            Assert.True(page.State.IsSaving);
            Assert.Null(await page.PrepareCloseAsync().WaitAsync(Bound));
            Assert.Null(page.OriginalClose);
            source.Release.TrySetResult();
            Assert.True(await save.WaitAsync(Bound));
            Assert.True(page.State.HasChanges);
            Assert.Equal("Typed during real held save", page.State.Draft.Description);
            var saved = (await files.Registry().GetAsync(space.Id))!;
            Assert.Equal("Captured save", saved.Name);
            Assert.Equal("", saved.Description);
        }
        finally
        {
            source.Release.TrySetResult();
            try { if (save is not null) await save.WaitAsync(Bound); }
            finally { await page.CloseAndDrainAsync().WaitAsync(Bound); }
        }
    }

    [AvaloniaFact]
    public async Task Real_conflict_in_decision_save_keeps_both_saved_record_and_local_draft_until_cancel()
    {
        using var files = new Files();
        var registry = files.Registry(); var space = await registry.CreateAsync("Original");
        var page = new SpaceDetailsPage(registry, space.Id, Ready.Instance);
        using var view = new MountedPage(page);
        try
        {
            await page.ActivateAsync().WaitAsync(Bound);
            page.TryEdit(SpaceDetailsField.Name, "Local unsaved");
            await files.Registry().UpdateAsync(space with { Name = "Other editor saved" }, space.Revision);
            var prepare = page.PrepareCloseAsync(); await DecisionReady(page);
            var host = page.DecisionHost!; var show = page.DecisionShow!;
            Click(host, "space-details-close-save");
            await host.CaptureOriginalActionSettlement(show).WaitAsync(Bound);
            Assert.False(prepare.IsCompleted);
            Assert.True(page.State.HasConflict);
            Assert.Equal("Local unsaved", page.State.Draft.Name);
            Assert.Equal("Other editor saved", (await files.Registry().GetAsync(space.Id))!.Name);
            Click(host, "space-details-close-cancel");
            Assert.Null(await prepare.WaitAsync(Bound));
            Assert.Null(page.OriginalClose);
            Assert.True(page.TryEdit(SpaceDetailsField.Description, "Draft still live"));
        }
        finally { await page.CloseAndDrainAsync().WaitAsync(Bound); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_source_or_accepted_preparation_restore_failure_releases_lease_and_retains_exact_cause(bool failRestore)
    {
        using var files = new Files(); var source = new ControlledStore(files.Store());
        var registry = new SpaceRegistry(source); var space = await registry.CreateAsync("Original");
        var page = new SpaceDetailsPage(registry, space.Id, Ready.Instance);
        using var view = new MountedPage(page);
        var failure = new IOException("Controlled actual settings read failure");
        Exception? observed = null;
        var restoreFailed = false;
        void OnPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs change)
        {
            if (failRestore && !restoreFailed && change.Property == ContentControl.ContentProperty && ReferenceEquals(page.Content, page.EditorHost))
            {
                restoreFailed = true;
                throw failure; // Actual native publication callback, after Save accepted its exact lease.
            }
        }
        try
        {
            await page.ActivateAsync().WaitAsync(Bound);
            page.TryEdit(SpaceDetailsField.Description, "Never discard on failed source");
            var draft = page.State.Draft;
            var prepare = page.PrepareCloseAsync(); await DecisionReady(page);
            page.PropertyChanged += OnPropertyChanged;
            if (!failRestore) source.FailExport = failure;
            Click(page.DecisionHost!, failRestore ? "space-details-close-save" : "space-details-close-discard");
            observed = await Assert.ThrowsAnyAsync<Exception>(() => prepare.WaitAsync(Bound));
            Assert.True(ContainsSame(observed, failure));
            Assert.Equal(draft, page.State.Draft);
            Assert.Null(page.OriginalClose);
            Assert.True(page.TryEdit(SpaceDetailsField.Name, "Editable after failed preparation"));
            Assert.Equal(failRestore ? "Never discard on failed source" : "", (await files.Registry().GetAsync(space.Id))!.Description);
        }
        finally
        {
            page.PropertyChanged -= OnPropertyChanged;
            var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => page.CloseAndDrainAsync().WaitAsync(Bound));
            Assert.True(ContainsSame(closeFailure, failure)); // Failed original is never waived as clean.
            Assert.NotNull(observed);
        }
    }

    [AvaloniaFact]
    public async Task Foreign_stale_and_aborted_leases_never_start_close_and_caller_withdrawal_restores_actual_editor()
    {
        using var files = new Files(); var registry = files.Registry(); var space = await registry.CreateAsync("Original");
        var first = new SpaceDetailsPage(registry, space.Id, Ready.Instance);
        var second = new SpaceDetailsPage(registry, space.Id, Ready.Instance);
        using var firstView = new MountedPage(first);
        using var secondView = new MountedPage(second);
        try
        {
            await first.ActivateAsync().WaitAsync(Bound); await second.ActivateAsync().WaitAsync(Bound);
            var lease = (await first.PrepareCloseAsync().WaitAsync(Bound))!;
            Assert.False(await second.CommitCloseAsync(lease).WaitAsync(Bound));
            Assert.Null(second.OriginalClose);
            Assert.True(await first.AbortCloseAsync(lease).WaitAsync(Bound));
            Assert.False(await first.CommitCloseAsync(lease).WaitAsync(Bound));
            Assert.Null(first.OriginalClose);
            Assert.True(first.TryEdit(SpaceDetailsField.Name, "After sibling veto"));
            using var caller = new CancellationTokenSource();
            var prepare = first.PrepareCloseAsync(caller.Token); await DecisionReady(first);
            caller.Cancel();
            Assert.Null(await prepare.WaitAsync(Bound));
            Assert.Same(first.EditorHost, first.Content);
            Assert.Equal("After sibling veto", first.State.Draft.Name);
            Assert.True(first.TryEdit(SpaceDetailsField.Description, "Editable after withdrawn decision"));
            // Healthy Cancel/reopen must release the actual closed host graphs, not
            // merely hide them in a growing owner list after reporting clean closes.
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var again = first.PrepareCloseAsync(); await DecisionReady(first);
                var actualHost = first.DecisionHost!;
                Click(actualHost, "space-details-close-cancel");
                Assert.Null(await again.WaitAsync(Bound));
                Assert.True(actualHost.OriginalClose!.IsCompletedSuccessfully);
                var retained = (System.Collections.ICollection)typeof(SpaceDetailsPage).GetField("_decisions",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(first)!;
                Assert.Empty(retained);
                Assert.Same(first.EditorHost, first.Content);
            }
        }
        finally
        {
            try { await first.CloseAndDrainAsync().WaitAsync(Bound); }
            finally { await second.CloseAndDrainAsync().WaitAsync(Bound); }
        }
    }

    [AvaloniaFact]
    public async Task External_retirement_joins_pending_decision_and_both_actual_CUI_hosts_without_self_join()
    {
        using var files = new Files(); var registry = files.Registry(); var space = await registry.CreateAsync("Original");
        var page = new SpaceDetailsPage(registry, space.Id, Ready.Instance);
        using var view = new MountedPage(page);
        Task<SpaceDetailsPage.ClosePreparation?>? prepare = null;
        try
        {
            await page.ActivateAsync().WaitAsync(Bound);
            page.TryEdit(SpaceDetailsField.Name, "Unsaved pending decision");
            prepare = page.PrepareCloseAsync(); await DecisionReady(page);
            var actualDecisionHost = page.DecisionHost!;
            await page.CloseAndDrainAsync().WaitAsync(Bound);
            Assert.Null(await prepare.WaitAsync(Bound));
            Assert.True(actualDecisionHost.OriginalClose!.IsCompletedSuccessfully);
            Assert.True(page.EditorHost.OriginalClose!.IsCompletedSuccessfully);
            Assert.Null(page.Content);
            Assert.Equal("Original", (await files.Registry().GetAsync(space.Id))!.Name);
        }
        finally { await page.CloseAndDrainAsync().WaitAsync(Bound); }
    }

    [AvaloniaFact]
    public async Task External_retirement_joins_held_actual_save_and_keeps_its_exact_source_fault()
    {
        using var files = new Files();
        var source = new ControlledStore(files.Store()); var registry = new SpaceRegistry(source);
        var space = await registry.CreateAsync("Original");
        var page = new SpaceDetailsPage(registry, space.Id, Ready.Instance);
        using var view = new MountedPage(page);
        Task<bool>? save = null;
        Task? close = null;
        var failure = new IOException("Actual acquired settings operation failed after retirement");
        try
        {
            await page.ActivateAsync().WaitAsync(Bound);
            Assert.True(page.TryEdit(SpaceDetailsField.Name, "Pending source"));
            source.Hold = true;
            source.FaultAfterRelease = failure;
            save = page.SaveAsync();
            await source.Entered.Task.WaitAsync(Bound);
            close = page.CloseAndDrainAsync();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).GetTask().WaitAsync(Bound);
            Assert.False(save.IsCompleted);
            Assert.False(close.IsCompleted); // Cancellation is not actual source settlement.
            Assert.False(page.TryEdit(SpaceDetailsField.Description, "Retired admission"));
            source.Release.TrySetResult();
            var saveFailure = await Assert.ThrowsAnyAsync<Exception>(() => save.WaitAsync(Bound));
            var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(Bound));
            Assert.True(ContainsSame(saveFailure, failure));
            Assert.True(ContainsSame(closeFailure, failure));
            Assert.Equal("Original", (await files.Registry().GetAsync(space.Id))!.Name);
        }
        finally
        {
            source.Release.TrySetResult();
            try
            {
                if (save is not null)
                    await Assert.ThrowsAnyAsync<Exception>(() => save.WaitAsync(Bound));
            }
            finally
            {
                await Assert.ThrowsAnyAsync<Exception>(() => (close ?? page.CloseAndDrainAsync()).WaitAsync(Bound));
            }
        }
    }

    private static async Task DecisionReady(SpaceDetailsPage page)
    {
        using var deadline = new CancellationTokenSource(Bound);
        while (page.DecisionHost?.Availability?.State != CuiSceneAvailabilityState.Ready ||
            !ReferenceEquals(page.Content, page.DecisionHost))
        {
            deadline.Token.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).GetTask().WaitAsync(deadline.Token);
        }
        Dispatcher.UIThread.RunJobs();
    }
    private static T Find<T>(CuiSceneHost host, string id) where T : Control
    {
        // Inputs inside the authored scrolling container acquire their visual
        // children through the actual mounted templates/layout, as in the canonical
        // SpaceDetailsCuiTests. This is a headless Window, not a native app launch.
        var window = Assert.IsType<Window>(TopLevel.GetTopLevel(host));
        for (var pass = 0; pass < 3; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
        }
        var root = Assert.IsAssignableFrom<Control>(host.Content);
        return root.GetVisualDescendants().Prepend(root).OfType<T>().Single(control =>
            control.Name == id || AutomationProperties.GetAutomationId(control) == id);
    }
    private static TextBox Input(CuiSceneHost host, string id) => Find<TextBox>(host, id);
    private static void Click(CuiSceneHost host, string id)
    {
        Dispatcher.UIThread.RunJobs();
        var button = Find<Button>(host, id); Assert.True(button.IsEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static bool ContainsSame(Exception actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException aggregate && aggregate.InnerExceptions.Any(error => ContainsSame(error, expected)) ||
        actual.InnerException is { } inner && ContainsSame(inner, expected);

    private sealed class MountedPage : IDisposable
    {
        private readonly Window _window;
        internal MountedPage(SpaceDetailsPage page)
        {
            _window = new Window { Content = page, Width = 900, Height = 800 };
            _window.Show();
        }
        public void Dispose()
        {
            _window.Content = null;
            _window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class Ready : ICuiSceneReadiness
    {
        internal static readonly Ready Instance = new();
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => new(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready,
            "component-fixture", "Explicit headless fixture, not installed Home admission."));
    }
    private sealed class Files : IAppPaths, IDisposable
    {
        public Files()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "9to1-space-details-page-" + Guid.NewGuid().ToString("N"));
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(DataDirectory);
            else Directory.CreateDirectory(DataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "unused-browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "unused-attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "unused-logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "unused-legacy");
        public VersionedAtomicSettingsStore Store() => new(this);
        public SpaceRegistry Registry() => new(Store());
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
    private sealed class ControlledStore(VersionedAtomicSettingsStore inner) : IVersionedSettingsStore, IVersionedSettingsGuardedCompareExchange
    {
        internal bool Hold;
        internal IOException? FailExport;
        internal IOException? FaultAfterRelease;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => inner.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => inner.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => inner.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token)
        {
            if (FailExport is { } error) { FailExport = null; return Task.FromException<SettingsExportManifest>(error); }
            return inner.ExportAsync(token);
        }
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => inner.ImportAsync(value, token);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken token) =>
            inner.CompareExchangeAsync(key, expected, replacement, token);
        public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected, string? replacement,
            IReadOnlyDictionary<string, string?> guards, CancellationToken token)
        {
            if (Hold && key == "spaces.registry")
            {
                Hold = false;
                Entered.TrySetResult();
                // A genuinely acquired source may ignore cancellation. The page must
                // join this raw operation before claiming retirement, even if it faults.
                await Release.Task;
                if (FaultAfterRelease is { } failure) throw failure;
            }
            return await inner.CompareExchangeGuardedAsync(key, expected, replacement, guards, token);
        }
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected, string? replacement,
            IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token) =>
            inner.CompareExchangeGuardedAsync(key, expected, replacement, guards, admission, token);
    }
}
