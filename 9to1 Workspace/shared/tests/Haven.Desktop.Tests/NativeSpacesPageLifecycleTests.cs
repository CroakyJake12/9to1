using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HavenOS.Apps.Spaces;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Spaces;
using Haven.UI;

namespace Haven.Desktop.Tests;

/// <summary>Real native scene/page callbacks with controlled source Tasks. These controls
/// verify presentation custody, not live permissions, provider execution or package acceptance.</summary>
public sealed class NativeSpacesPageLifecycleTests
{
    [AvaloniaFact]
    public async Task Confirmed_delete_invokes_the_existing_owner_once_and_preserves_target()
    {
        var registry = new SpaceRegistry(new Settings());
        var custom = await registry.CreateAsync("Delete callback target");
        var calls = new List<Guid>();
        var page = new NativeSpacesPage(registry, null, null, deleteSpace: id =>
        { calls.Add(id); return Task.CompletedTask; });
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            page.OriginalScene.ConfirmDelete(custom.Id);
            await page.LatestOriginalAction!;
            Assert.Equal(custom.Id, Assert.Single(calls));
            // The configured owner remains authoritative; the page must not also delete locally.
            Assert.False((await registry.GetAsync(custom.Id))!.IsArchived);
            await page.CloseAndDrainAsync();
            page.OriginalScene.ConfirmDelete(custom.Id);
            Assert.Single(calls);
        }
        finally { _ = await Record.ExceptionAsync(page.CloseAndDrainAsync); }
    }

    [AvaloniaFact]
    public async Task Closing_waits_for_the_actual_delete_task_before_detaching_the_scene()
    {
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var page = new NativeSpacesPage(new SpaceRegistry(new Settings()), null, null, deleteSpace: _ =>
        { calls++; entered.TrySetResult(); return raw.Task; });
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            page.OriginalScene.ConfirmDelete(Guid.NewGuid());
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var root = page.Scene.Root;
            page.RequestRetirement();
            var close = page.CloseAndDrainAsync();
            Assert.Same(close, page.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
            Assert.False(raw.Task.IsCompleted);
            Assert.Same(root, page.Scene.Root);
            raw.SetResult();
            await close;
            Assert.Equal(1, calls);
            Assert.Null(page.Scene.Root);
            Assert.Null(page.Content);
            Assert.Throws<ObjectDisposedException>(() => { _ = page.ActivateAsync(CancellationToken.None); });
        }
        finally
        {
            raw.TrySetResult();
            _ = await Record.ExceptionAsync(page.CloseAndDrainAsync);
        }
    }

    [AvaloniaFact]
    public async Task Handled_delete_faults_keep_all_actual_sibling_causes_in_the_external_drain()
    {
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new IOException("delete source failure");
        var second = new OperationCanceledException("faulted source payload, not canceled Task");
        var page = new NativeSpacesPage(new SpaceRegistry(new Settings()), null, null, deleteSpace: _ =>
        { entered.TrySetResult(); return raw.Task; });
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            page.OriginalScene.ConfirmDelete(Guid.NewGuid());
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            raw.SetException([first, second]);
            await page.LatestOriginalAction!; // Existing handled UI policy can return; custody still retains failures.
            var error = await Record.ExceptionAsync(page.CloseAndDrainAsync);
            Assert.NotNull(error);
            Assert.True(ContainsSameCause(error!, first));
            Assert.True(ContainsSameCause(error!, second));
            Assert.Null(page.Scene.Root);
        }
        finally
        {
            raw.TrySetResult();
            _ = await Record.ExceptionAsync(page.CloseAndDrainAsync);
        }
    }

    [AvaloniaFact]
    public async Task Restored_execution_context_source_cannot_join_its_own_page_close()
    {
        var priorContext = ExecutionContext.Capture()!;
        Exception? refusal = null;
        NativeSpacesPage? page = null;
        page = new NativeSpacesPage(new SpaceRegistry(new Settings()), null, null, deleteSpace: _ =>
        {
            ExecutionContext.Run(priorContext, _ =>
            {
                page!.RequestRetirement();
                refusal = Record.Exception(() => page.CloseAndDrainAsync().GetAwaiter().GetResult());
            }, null);
            return Task.CompletedTask;
        });
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            page.OriginalScene.ConfirmDelete(Guid.NewGuid());
            await page.LatestOriginalAction!;
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.Contains("source callback", refusal!.Message);
            await page.CloseAndDrainAsync();
            Assert.Null(page.Scene.Root);
        }
        finally { _ = await Record.ExceptionAsync(page.CloseAndDrainAsync); }
    }

    [AvaloniaFact]
    public async Task Retired_held_refresh_is_joined_and_cannot_publish_stale_space_rows()
    {
        var settings = new Settings();
        var registry = new SpaceRegistry(settings);
        _ = await registry.GetAllAsync();
        var held = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        object? originalState = null;
        settings.ReadOverride = (_, value) => { originalState = value; entered.TrySetResult(); return held.Task; };
        var page = new NativeSpacesPage(registry);
        try
        {
            var activate = page.ActivateAsync(CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.RequestRetirement();
            var close = page.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            held.SetResult(originalState);
            await activate;
            await close;
            Assert.Empty(page.OriginalScene.SpaceRows.Children);
            Assert.Null(page.Scene.Root);
        }
        finally
        {
            held.TrySetResult(originalState);
            _ = await Record.ExceptionAsync(page.CloseAndDrainAsync);
        }
    }

    [AvaloniaFact]
    public void Scene_publication_rechecks_after_a_control_callback_withdraws_the_view()
    {
        var allowed = true;
        using var scene = new SpacesHavenScene(null, () =>
        { if (!allowed) throw new InvalidOperationException("withdrawn presentation"); });
        var originalSave = scene.Save.GetValue(HavenProperties.Enabled);
        scene.CreateSpace.Invalidated += (_, _) => allowed = false;
        Assert.Throws<InvalidOperationException>(() => scene.SetBusy(true));
        Assert.Equal(originalSave, scene.Save.GetValue(HavenProperties.Enabled));
    }

    [AvaloniaFact]
    public async Task Actual_home_CUI_details_action_queues_exact_target_and_settles_before_external_retirement()
    {
        var registry = new SpaceRegistry(new Settings());
        var target = await registry.CreateAsync("Real queued details");
        var host = new DetailsHost();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SpacesAction? captured = null;
        Task? settlement = null;
        var incompleteAtAdmission = false;
        var page = new SpacesHomePage(registry, host, SpacesHomePage.ImmediateReadinessInstance,
            queueNavigation: (action, actualSettlement) =>
            {
                captured = action;
                settlement = actualSettlement;
                incompleteAtAdmission = !actualSettlement.IsCompleted;
                entered.TrySetResult();
                return true;
            });
        var window = new Window { Width = 1280, Height = 900, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(CancellationToken.None);
            await page.Controller.SelectSpaceAsync(target.Id);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var button = Assert.Single(page.GetVisualDescendants().OfType<Button>(), b => b.Name == "edit-space-details");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(SpacesActionKind.EditDetails, captured!.Kind);
            Assert.Equal(target.Id, captured.Context.Scope.RegisteredSpaceId);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(target), System.Text.Json.JsonSerializer.Serialize(captured.Context.RegisteredSpace));
            Assert.True(incompleteAtAdmission, "The real originating command and loader must still own admission.");
            await settlement!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(settlement.IsCompletedSuccessfully);
            Assert.Empty(host.Actions); // Navigation belongs to the queue, not a second direct dispatch.
            Assert.NotNull(page.Content);
            Assert.True(await page.TryPrepareToCloseAsync(CancellationToken.None));
            var close = page.CloseAndDrainAsync();
            Assert.Same(close, page.CloseAndDrainAsync());
            await close.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(page.Content);
        }
        finally
        {
            _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Home_queue_does_not_manufacture_details_support_for_a_legacy_inner_host()
    {
        var registry = new SpaceRegistry(new Settings());
        var target = await registry.CreateAsync("Unsupported details");
        var calls = 0;
        var page = new SpacesHomePage(registry, new LegacyHost(), SpacesHomePage.ImmediateReadinessInstance,
            queueNavigation: (_, _) => { calls++; return true; });
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            await page.Controller.SelectSpaceAsync(target.Id);
            Assert.False(page.Controller.State.Selection!.CanEditDetails);
            Assert.False(page.IsActionAvailable("spaces.edit-details"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => page.DispatchAsync("spaces.edit-details", target.Id).AsTask());
            Assert.Equal(0, calls);
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5))); }
    }

    [AvaloniaFact]
    public async Task Pending_home_binding_refresh_settles_after_close_without_publishing_to_retired_page()
    {
        var registry = new SpaceRegistry(new Settings());
        var target = await registry.CreateAsync("Pending refresh target");
        var page = new SpacesHomePage(registry, new LegacyHost(), SpacesHomePage.ImmediateReadinessInstance);
        var publications = 0;
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            Dispatcher.UIThread.RunJobs(); // Settle setup before acquiring the new controlled cohort.
            page.PropertyChanged += CountPublication;
            var selection = page.Controller.SelectSpaceAsync(target.Id);
            Assert.True(selection.IsCompletedSuccessfully);
            Assert.Equal(0, publications); // The actual UI refresh dispatcher callbacks are still queued.
            var close = page.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            await close.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, publications);
            Assert.Null(page.Content);
        }
        finally
        {
            page.PropertyChanged -= CountPublication;
            _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        }
        void CountPublication(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => publications++;
    }

    [AvaloniaFact]
    public async Task Actual_home_binding_callback_fault_is_retained_by_external_close()
    {
        var registry = new SpaceRegistry(new Settings());
        var target = await registry.CreateAsync("Binding fault target");
        var page = new SpacesHomePage(registry, new LegacyHost(), SpacesHomePage.ImmediateReadinessInstance);
        var cause = new IOException("Actual binding observer failure");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
            page.PropertyChanged += FailPublication;
            await page.Controller.SelectSpaceAsync(target.Id);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var error = await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.NotNull(error);
            Assert.True(ContainsSameCause(error!, cause));
            Assert.False(page.OriginalClose!.IsCompletedSuccessfully);
        }
        finally
        {
            page.PropertyChanged -= FailPublication;
            _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        }
        void FailPublication(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        { entered.TrySetResult(); throw cause; }
    }

    private sealed class DetailsHost : ISpacesActionHost, ISpacesActionSupport
    {
        public List<SpacesAction> Actions { get; } = [];
        public bool SupportsAction(SpacesActionKind action) => action is SpacesActionKind.Open or SpacesActionKind.EditDetails;
        public Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken = default)
        { Actions.Add(action); return Task.CompletedTask; }
    }

    private sealed class LegacyHost : ISpacesActionHost
    {
        public Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unsupported details must never reach the legacy host.");
    }

    private static bool ContainsSameCause(Exception actual, Exception sought) =>
        ReferenceEquals(actual, sought) || actual is AggregateException group &&
        group.InnerExceptions.Any(child => ContainsSameCause(child, sought));

    private sealed class Settings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];
        internal Func<string, object?, Task<object?>>? ReadOverride;
        public async Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
        {
            var value = _values.GetValueOrDefault(key);
            return ReadOverride is null ? value as T : await ReadOverride(key, value) as T;
        }
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class
        { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken token)
        { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => throw new NotSupportedException();
    }
}
