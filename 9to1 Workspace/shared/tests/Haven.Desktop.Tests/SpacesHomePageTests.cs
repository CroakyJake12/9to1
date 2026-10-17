using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Spaces;

namespace Haven.Desktop.Tests;

/// <summary>
/// Lifetime acceptance for the CUI-based Spaces Home page. These tests use the real
/// <see cref="SpacesHomePage"/>, the actual authored <c>SpacesHome.cui</c> surface, and an
/// independently owned navigation queue callback so the page never awaits downstream navigation.
/// </summary>
public sealed class SpacesHomePageTests
{
    private static Button FindButton(Control root, string name) =>
        root.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);

    private static async Task ClickAsync(Window window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
        Assert.NotNull(point);
        window.MouseMove(point.Value);
        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
    }

    private static async Task WaitForStatusAsync(SpacesHomeController controller, Func<string, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate(controller.State.StatusMessage)) return;
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(predicate(controller.State.StatusMessage), $"Timed out waiting for status. Current: {controller.State.StatusMessage}");
    }

    [AvaloniaFact]
    public async Task Actual_CUI_Open_action_queues_navigation_and_settles_before_its_own_surface_retirement()
    {
        var registry = new SpaceRegistry(new Settings());
        var target = await registry.CreateAsync("Queue target");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SpacesAction? capturedAction = null;
        Task? capturedSettlement = null;

        Func<SpacesAction, Task, bool> queue = (action, settlement) =>
        {
            capturedAction = action;
            capturedSettlement = settlement;
            entered.TrySetResult();
            release.Task.Wait();
            return true;
        };

        var page = new SpacesHomePage(registry, new NoOpActionHost(), SpacesHomePage.ImmediateReadinessInstance, queueNavigation: queue);
        var window = new Window { Width = 1280, Height = 900, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(CancellationToken.None);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var openButton = FindButton((Control)page.OriginalHost.Content!, "open-custom-space");
            await ClickAsync(window, openButton);

            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(capturedAction);
            Assert.Equal(SpacesActionKind.Open, capturedAction!.Kind);
            Assert.Equal(target.Id, capturedAction.Context.Scope.RegisteredSpaceId);
            Assert.NotNull(capturedSettlement);
            Assert.False(capturedSettlement!.IsCompleted, "Settlement must not complete while the originating action is still held.");

            release.SetResult();
            await capturedSettlement.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(capturedSettlement.IsCompletedSuccessfully);

            var close = page.CloseAndDrainAsync();
            Assert.Same(close, page.CloseAndDrainAsync());
            await close;
            Assert.Null(page.Content);
        }
        finally
        {
            release.TrySetResult();
            _ = await Record.ExceptionAsync(page.CloseAndDrainAsync);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Rejected_queue_admission_is_observable_through_controller_status()
    {
        var registry = new SpaceRegistry(new Settings());
        var target = await registry.CreateAsync("Refused target");

        var page = new SpacesHomePage(registry, new NoOpActionHost(), SpacesHomePage.ImmediateReadinessInstance,
            queueNavigation: (_, _) => false);
        var window = new Window { Width = 1280, Height = 900, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(CancellationToken.None);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            await page.DispatchAsync("spaces.open", target.Id);
            await WaitForStatusAsync(page.Controller, s =>
                s.Contains("Could not open", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(5));

            Assert.DoesNotContain("Opened", page.Controller.State.StatusMessage);
            Assert.DoesNotContain("Opening", page.Controller.State.StatusMessage);
        }
        finally
        {
            _ = await Record.ExceptionAsync(page.CloseAndDrainAsync);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Retired_page_rejects_open_action_without_calling_queue()
    {
        var registry = new SpaceRegistry(new Settings());
        var target = await registry.CreateAsync("Stale target");

        var called = false;
        var page = new SpacesHomePage(registry, new NoOpActionHost(), SpacesHomePage.ImmediateReadinessInstance,
            queueNavigation: (_, _) => { called = true; return true; });
        var window = new Window { Width = 1280, Height = 900, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(CancellationToken.None);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            page.RequestRetirement();
            var error = await Record.ExceptionAsync(() => page.DispatchAsync("spaces.open", target.Id).AsTask());
            Assert.NotNull(error);
            Assert.False(called, "The queue callback must not be invoked after the page is retired.");
        }
        finally
        {
            _ = await Record.ExceptionAsync(page.CloseAndDrainAsync);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Repeated_close_calls_return_same_task_identity()
    {
        var registry = new SpaceRegistry(new Settings());
        var page = new SpacesHomePage(registry, new NoOpActionHost(), SpacesHomePage.ImmediateReadinessInstance);
        var window = new Window { Width = 1280, Height = 900, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(CancellationToken.None);

            var first = page.CloseAndDrainAsync();
            var second = page.CloseAndDrainAsync();
            Assert.Same(first, second);
            Assert.Same(first, page.OriginalClose);
            await first;
        }
        finally
        {
            _ = await Record.ExceptionAsync(page.CloseAndDrainAsync);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Close_retains_actual_host_readiness_failure_and_preserves_view()
    {
        var registry = new SpaceRegistry(new Settings());
        var cause = new IOException("Readiness failure");
        var page = new SpacesHomePage(registry, new NoOpActionHost(), new FaultyReadiness(cause));
        var window = new Window { Width = 1280, Height = 900, Content = page };
        try
        {
            window.Show();
            var activateError = await Record.ExceptionAsync(() => page.ActivateAsync(CancellationToken.None));
            Assert.NotNull(activateError);

            // The external drain must retain the actual readiness cause, not replace it with a
            // generic acknowledgement message. The view stays inspectable because cleanup is
            // skipped when the real host close is faulted.
            var close = page.CloseAndDrainAsync();
            var closeError = await Record.ExceptionAsync(() => close);
            Assert.NotNull(closeError);
            Assert.True(ContainsSameCause(closeError!, cause));
            Assert.Same(close, page.OriginalClose);
            Assert.Same(page, window.Content);
        }
        finally
        {
            _ = await Record.ExceptionAsync(page.CloseAndDrainAsync);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Rename_draft_or_editor_operation_vetoes_close()
    {
        var registry = new SpaceRegistry(new Settings());
        var page = new SpacesHomePage(registry, new NoOpActionHost(), SpacesHomePage.ImmediateReadinessInstance);
        var window = new Window { Width = 1280, Height = 900, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(CancellationToken.None);

            await page.Controller.CreateCustomSpaceAsync();
            Assert.True(page.Controller.State.PendingRenameSpaceId.HasValue);
            Assert.False(await page.TryPrepareToCloseAsync());

            page.Controller.CancelRename();
            Assert.True(await page.TryPrepareToCloseAsync());
        }
        finally
        {
            _ = await Record.ExceptionAsync(page.CloseAndDrainAsync);
            window.Close();
        }
    }

    private static bool ContainsSameCause(Exception actual, Exception sought) =>
        ReferenceEquals(actual, sought) || actual is AggregateException group &&
        group.InnerExceptions.Any(child => ContainsSameCause(child, sought));

    private sealed class Settings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
            => Task.FromResult(_values.TryGetValue(key, out var value) ? value as T : null);

        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken token)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class NoOpActionHost : ISpacesActionHost
    {
        public Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FaultyReadiness(Exception cause) : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
            => throw cause;
    }
}
