using Haven.Core;
using Haven.Application;
using Haven.Application.Automations;
using Microsoft.Extensions.DependencyInjection;
using Haven.Desktop.Views.Pages.Tasks;
using Haven.Desktop.Views.Pages.Automations;
using Haven.Desktop.Views.Shell.NativePresentation;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    public void OpenTasksDashboard()
    {
        var containerId = CurrentChat.SelectedContainer?.Id;
        var key = "haven-tasks-" + (containerId?.ToString("N") ?? "global");
        var existing = OpenTabs.FirstOrDefault(item =>
            item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        var page = new NativeTasksSpacePage(
            _conversations,
            StartOneTimeTaskAsync,
            InvokeTaskAsync,
            OpenNativeConversationAsync);

        AddOrSelectTab(
            key,
            "Haven Tasks",
            page,
            closeable: true,
            surface: HavenSurface.Tasks);
    }

    private readonly SemaphoreSlim _automationOpen = new(1, 1);
    public async void OpenAutomationsDashboard()
    {
        try { await OpenAutomationsDashboardAsync(CancellationToken.None); }
        catch (Exception error) when (error is not OperationCanceledException)
        { System.Diagnostics.Debug.WriteLine("Automations owner is unavailable: " + error.Message); }
    }
    internal async Task OpenAutomationsDashboardAsync(CancellationToken token)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(MainView));
        var services = App.Services ?? throw new InvalidOperationException("Actual Automation host services are unavailable.");
        var actors = services.GetRequiredService<IAuthenticatedResourceActorSource>();
        var containerId = CurrentChat.SelectedContainer?.Id;
        var originalActor = await actors.GetCurrentAsync(token) ?? throw new UnauthorizedAccessException("Original actor is unavailable.");
        var caller = services.GetRequiredService<IAutomationDefinitionReviewCaller>();
        var originalSelection = await caller.CaptureAsync(originalActor, token);
        await _automationOpen.WaitAsync(token);
        NativeAutomationsPage? candidate = null;
        try
        {
            await caller.RequireCurrentAsync(originalSelection, token);
            if (IsDisposed) throw new ObjectDisposedException(nameof(MainView));
            var key = "haven-automations-" + (containerId?.ToString("N") ?? "global");
            var existing = OpenTabs.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                if (existing.Page is not NativeAutomationsPage page) throw new InvalidOperationException("Original automation page is unavailable.");
                await page.RequireCurrentAsync(originalActor, token);
                if (IsDisposed) throw new ObjectDisposedException(nameof(MainView));
                SelectedTab = existing; return;
            }
            candidate = new NativeAutomationsPage(_workspaceState, _automations, containerId, StartOneTimeTaskAsync,
                InvokeTaskAsync, _versionedSettings, caller, originalSelection);
            await candidate.RequireCurrentAsync(originalActor, token);
            if (IsDisposed) throw new ObjectDisposedException(nameof(MainView));
            AddOrSelectTab(key, "Automations", candidate, closeable: true, surface: HavenSurface.Automations);
            candidate = null;
        }
        finally { candidate?.Dispose(); _automationOpen.Release(); }
    }

    private async Task InvokeTaskAsync(string instruction)
    {
        if (_edition == HavenShellEdition.New)
        {
            var page = CreateNewChatPage();
            page.ConfigureTaskMode();
            await ConfigureAddMenuAsync(page);
            AddOrSelectTab(
                "task-run-" + Guid.NewGuid().ToString("N")[..8],
                "Run Task",
                page,
                true,
                HavenSurface.Tasks,
                forceNewTab: true);
            ApplyShellVisualState();
            page.Submit(instruction);
            return;
        }

        AddOrSelectTab(
            "chat-tasks",
            "Run Task",
            CurrentChat,
            false,
            HavenSurface.Tasks);
        await CurrentChat.InvokeAsync(instruction);
    }

    private async Task StartOneTimeTaskAsync()
    {
        var page = CreateNewChatPage();
        page.ConfigureTaskMode();
        await ConfigureAddMenuAsync(page);
        AddOrSelectTab(
            "task-run-" + Guid.NewGuid().ToString("N")[..8],
            "Run Task",
            page,
            true,
            HavenSurface.Tasks,
            forceNewTab: true);
        ApplyShellVisualState();
        await RefreshNativeChatSidebarAsync();
        page.FocusComposer();
    }
}
