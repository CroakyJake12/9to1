using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>Creates normal Haven windows and chrome-free pop-ups while moving the same tab session.</summary>
public sealed partial class WorkspaceWindowService(
    IServiceProvider services,
    WorkspaceSessionCoordinator sessions,
    NotificationService notifications) : IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard, IAsyncDisposable
{
    private readonly List<Window> _windows = [];

    public void OpenInNewWindow(MainView source, WorkspaceTabViewModel tab) =>
        RunWindowSource(() => OpenInNewWindowCore(source, tab));

    private void OpenInNewWindowCore(MainView source, WorkspaceTabViewModel tab)
    {
        var moved = RetainDetachedTab(source.DetachTabForMove(tab));
        if (OriginalWindowWork.IsRetiring) return;
        var shell = RetainShell(AcquireWindowSource(() => services.GetRequiredService<MainView>()));
        if (OriginalWindowWork.IsRetiring) return;
        shell.ApplyEdition(source.Edition);
        if (OriginalWindowWork.IsRetiring) return;
        shell.AttachTransferredTab(moved, replaceExisting: true);
        ReleaseTransferredTab(moved);
        if (OriginalWindowWork.IsRetiring) return;
        sessions.Register(shell, WorkspaceWindowKind.Normal);
        var window = CreateMainWindow();
        window.DataContext = shell;
        if (OriginalWindowWork.IsRetiring) return;
        window.Title = moved.Title;

        window.Closed += (_, _) => ObserveWindowClosed(() => { _windows.Remove(window); sessions.Unregister(shell); });
        _windows.Add(window);
        window.Show();
        if (OriginalWindowWork.IsRetiring) return;
        _ = InitializeSecondaryShellAsync(shell);
    }

    public void OpenInPopUp(MainView source, WorkspaceTabViewModel tab) =>
        RunWindowSource(() => OpenInPopUpCore(source, tab));

    private void OpenInPopUpCore(MainView source, WorkspaceTabViewModel tab)
    {
        var moved = RetainDetachedTab(source.DetachTabForMove(tab));
        if (OriginalWindowWork.IsRetiring) return;
        var windowId = Guid.NewGuid();
        var layoutId = Guid.NewGuid();
        var paneId = Guid.NewGuid();
        var host = new ContentControl { Content = moved.Page };
        AutomationProperties.SetName(host, $"{moved.Title} pop-up surface");
        var window = RetainWindow(new Window());
        ConfigureWindow(() =>
        {
            window.Title = moved.Title;
            window.Width = 960;
            window.Height = 720;
            window.MinWidth = 420;
            window.MinHeight = 320;
            window.Content = host;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        });
        sessions.RegisterPopUp(
            windowId,
            () => source.CreateTabSnapshot(moved),
            () => new WorkspaceWindowSnapshot(
                windowId,
                WorkspaceWindowKind.PopUp,
                new WorkspaceLayoutSnapshot(layoutId, WorkspaceLayoutKind.Single, SplitOrientation.Vertical, 1d,
                    [new WorkspacePaneSnapshot(paneId, moved.SessionId, 0)]),
                [moved.SessionId],
                moved.SessionId,
                SerializeWindowBounds(window),
                DateTimeOffset.UtcNow));
        WireWindowGeometry(window);
        RetainPopup(window, host, moved, source);
        window.Closed += (_, _) => ObserveWindowClosed(() =>
        {
            sessions.UnregisterPopUp(windowId);
            host.Content = null;
            _windows.Remove(window);
            if (OriginalWindowWork.IsRetiring) return;
            if (!source.IsDisposed)
            {
                source.AttachTransferredTab(moved, replaceExisting: false);
                ReleaseTransferredTab(moved);
            }
            else
            {
                var shell = RetainShell(AcquireWindowSource(() => services.GetRequiredService<MainView>()));
                if (OriginalWindowWork.IsRetiring) return;
                shell.ApplyEdition(source.Edition);
                if (OriginalWindowWork.IsRetiring) return;
                shell.AttachTransferredTab(moved, replaceExisting: true);
                ReleaseTransferredTab(moved);
                sessions.Register(shell, WorkspaceWindowKind.Normal);
                var fallback = CreateMainWindow();
                fallback.DataContext = shell;
                if (OriginalWindowWork.IsRetiring) return;
                fallback.Title = moved.Title;
                fallback.Closed += (_, _) => ObserveWindowClosed(() => { _windows.Remove(fallback); sessions.Unregister(shell); });
                _windows.Add(fallback);
                fallback.Show();
                if (OriginalWindowWork.IsRetiring) return;
                _ = InitializeSecondaryShellAsync(shell);
            }
        });
        _windows.Add(window);
        if (TopLevel.GetTopLevel(source) is Window owner) window.Show(owner); else window.Show();
    }

    public Task RestoreAdditionalWindowsAsync(
        WorkspaceSessionSnapshot snapshot, HavenShellEdition edition, CancellationToken cancellationToken) =>
        OriginalWindowWork.RunAsync(original => RestoreAdditionalWindowsCoreAsync(original, snapshot, edition, cancellationToken));

    private async Task RestoreAdditionalWindowsCoreAsync(DesktopOriginalWorkLifetime.Original original,
        WorkspaceSessionSnapshot snapshot, HavenShellEdition edition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        foreach (var saved in snapshot.Windows.Where(item => item.Kind != WorkspaceWindowKind.Main))
        {
            if (OriginalWindowWork.IsRetiring) return;
            cancellationToken.ThrowIfCancellationRequested();
            if (saved.OrderedTabIds.Count == 0) continue;

            var shell = RetainShell(AcquireWindowSource(() => services.GetRequiredService<MainView>()));
            if (OriginalWindowWork.IsRetiring) return;
            shell.ApplyEdition(edition);
            if (OriginalWindowWork.IsRetiring) return;
            try
            {
                await JoinWindowOriginalAsync(AcquireWindowSource(() => shell.RestoreWorkspaceWindowAsync(snapshot, saved, cancellationToken)));
            }
            catch
            {
                await DrainWindowChildAsync(shell);
                continue;
            }

            if (OriginalWindowWork.IsRetiring) return;
            if (shell.SelectedTab is not { } selected)
            {
                await DrainWindowChildAsync(shell);
                continue;
            }

            if (saved.Kind == WorkspaceWindowKind.PopUp)
            {
                var moved = RetainDetachedTab(shell.DetachTabForMove(selected));
                await DrainWindowChildAsync(shell);
                if (OriginalWindowWork.IsRetiring) return;
                RestorePersistedPopUp(moved, saved);
                continue;
            }

            sessions.Register(shell, WorkspaceWindowKind.Normal, queueSave: false);
            var window = CreateMainWindow();
            window.DataContext = shell;
            if (OriginalWindowWork.IsRetiring) return;
            window.Title = selected.Title;
            ApplyWindowBounds(window, saved.BoundsJson);

            window.Closed += (_, _) => ObserveWindowClosed(() => { _windows.Remove(window); sessions.Unregister(shell); });
            _windows.Add(window);
            window.Show();
            _ = InitializeSecondaryShellAsync(shell);
        }
    }
    private void RestorePersistedPopUp(WorkspaceTabViewModel moved, WorkspaceWindowSnapshot saved)
    {
        RetainDetachedTab(moved);
        if (OriginalWindowWork.IsRetiring) return;
        var host = new ContentControl { Content = moved.Page };
        AutomationProperties.SetName(host, $"{moved.Title} pop-up surface");
        var window = RetainWindow(new Window());
        ConfigureWindow(() =>
        {
            window.Title = moved.Title; window.Width = 960; window.Height = 720;
            window.MinWidth = 420; window.MinHeight = 320; window.Content = host;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        });

        ApplyWindowBounds(window, saved.BoundsJson);
        sessions.RegisterPopUp(
            saved.Id,
            () => MainView.CreateDetachedTabSnapshot(moved),
            () => new WorkspaceWindowSnapshot(
                saved.Id,
                WorkspaceWindowKind.PopUp,
                saved.Layout,
                [moved.SessionId],
                moved.SessionId,
                SerializeWindowBounds(window),
                DateTimeOffset.UtcNow));

        WireWindowGeometry(window);
        RetainPopup(window, host, moved, null);
        window.Closed += (_, _) => ObserveWindowClosed(() =>
        {
            sessions.UnregisterPopUp(saved.Id);
            host.Content = null;
            _windows.Remove(window);
        });
        _windows.Add(window);
        window.Show();
    }

    internal static void ApplyWindowBounds(Window window, string? json)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var bounds = JsonSerializer.Deserialize<WindowBoundsState>(json);
            if (bounds is null ||
                !double.IsFinite(bounds.Width) ||
                !double.IsFinite(bounds.Height) ||
                bounds.Width <= 0 ||
                bounds.Height <= 0)
                return;

            var minimumWidth = double.IsFinite(window.MinWidth) && window.MinWidth > 0 ? window.MinWidth : 320d;
            var minimumHeight = double.IsFinite(window.MinHeight) && window.MinHeight > 0 ? window.MinHeight : 240d;
            window.Width = Math.Clamp(bounds.Width, minimumWidth, 7680d);
            window.Height = Math.Clamp(bounds.Height, minimumHeight, 4320d);

            if (Math.Abs(bounds.X) <= 32768 && Math.Abs(bounds.Y) <= 32768)
                window.Position = new PixelPoint(bounds.X, bounds.Y);
        }
        catch (JsonException)
        {
            // Old or corrupt geometry falls back to normal startup sizing.
        }
    }
    private Task InitializeSecondaryShellAsync(MainView shell) => OriginalWindowWork.RunAsync(async original =>
    {
        try
        {
            // The actual shell retains its own scoped sources; this service joins
            // the complete initialization original before retiring that shell.
            await JoinWindowOriginalAsync(AcquireWindowSource(() => shell.InitializeSecondaryWindowAsync(CancellationToken.None)));
        }
        catch (Exception ex)
        {
            if (!OriginalWindowWork.IsRetiring)
                AcquireWindowSource(() =>
                {
                    notifications.Show("Window initialization failed", SensitiveTextRedactor.Redact(ex.Message),
                        ToastKind.Error, TimeSpan.FromSeconds(12));
                    return true;
                });
            // AwaitAsync has already retained all actual source failures for close.
        }
    });

    private static string SerializeWindowBounds(Window window) => JsonSerializer.Serialize(new WindowBoundsState(
        window.Position.X, window.Position.Y, window.Width, window.Height));

    private sealed record WindowBoundsState(int X, int Y, double Width, double Height);

    private MainWindow CreateMainWindow()
    {
#if ANDROID
        return RetainWindow(new MainWindow());
#else
        return RetainWindow(new MainWindow(services.GetRequiredService<UserPreferencesService>()));
#endif
    }
}
