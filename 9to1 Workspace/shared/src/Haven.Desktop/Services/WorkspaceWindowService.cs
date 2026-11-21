using System.Text.Json;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
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
            if (OriginalWindowWork.IsRetiring) { PruneRetiredClosedPopup(window); return; }
            if (!source.IsDisposed)
            {
                source.AttachTransferredTab(moved, replaceExisting: false);
                ReleaseTransferredTab(moved, window);
            }
            else
            {
                var shell = RetainShell(AcquireWindowSource(() => services.GetRequiredService<MainView>()));
                if (OriginalWindowWork.IsRetiring) return;
                shell.ApplyEdition(source.Edition);
                if (OriginalWindowWork.IsRetiring) return;
                shell.AttachTransferredTab(moved, replaceExisting: true);
                ReleaseTransferredTab(moved, window);
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
            PruneRetiredClosedPopup(window);
        });
        _windows.Add(window);
        window.Show();
    }

    internal static void ApplyWindowBounds(Window window, string? json) =>
        ApplyWindowBounds(window, json, CurrentScreenWorkingArea(window));

    /// <summary>
    /// HAV-SHELL: a restored position must never strand the window's title/tab strip outside every
    /// connected screen (observed natively 20 November 2026 after a display re-arrangement: the frame
    /// landed above the top edge and the whole tab strip became unreachable). Size sanity clamps alone
    /// are not enough; the position is additionally clamped so the window remains reachable. A null
    /// working area (screens unreadable, e.g. headless or a pre-layout race) restores the saved value.
    /// </summary>
    internal static void ApplyWindowBounds(Window window, string? json, PixelRect? workingArea)
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

            var x = bounds.X;
            var y = bounds.Y;
            if (Math.Abs(x) <= 32768 && Math.Abs(y) <= 32768)
            {
                if (workingArea is { } area && area.Width > 0 && area.Height > 0)
                {
                    // Physical pixels throughout: leave at least a title-strip-width of window inside
                    // the combined working area, so the frame is always draggable back.
                    const int EdgeReach = 200, TitleReach = 64;
                    x = Math.Clamp(x, area.X, area.X + Math.Max(0, area.Width - EdgeReach));
                    y = Math.Clamp(y, area.Y, area.Y + Math.Max(0, area.Height - TitleReach));
                }
                window.Position = new PixelPoint(x, y);
                NoteExplicitPlacement(window);
            }
        }
        catch (JsonException)
        {
            // Old or corrupt geometry falls back to normal startup sizing.
        }
    }

    // On X11 the position property only tracks APP-driven moves (a WM-driven move is invisible to
    // it, both as event and as value). This table is how the code distinguishes "the app chose this
    // placement" from "the window manager made it up".
    private static readonly ConditionalWeakTable<Window, object> ExplicitPlacements = new();

    internal static void NoteExplicitPlacement(Window window) => ExplicitPlacements.GetValue(window, _ => new object());

    internal static bool HasExplicitPlacement(Window window) => ExplicitPlacements.TryGetValue(window, out _);

    /// <summary>One settled post-startup action: when nothing has deliberately placed the window,
    /// an explicit app-side move re-seats it centred on the primary working area (mutter honours
    /// app moves; reading the X11 position back cannot see its own strand). When something has,
    /// the explicit value is trusted and only re-validated.</summary>
    internal static void SeatMainWindowOnScreenIfImplicit(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.WindowState != WindowState.Normal) return;
        if (HasExplicitPlacement(window))
        {
            EnsureReachablePlacement(window);
            return;
        }
        var screens = window.Screens;
        if (screens is null) return;
        var areas = screens.All.Select(screen => screen.WorkingArea).ToArray();
        if (areas.Length == 0) return;
        var primary = screens.Primary?.WorkingArea ?? areas[0];
        if (primary.Width <= 0 || primary.Height <= 0) return;
        var scale = window.RenderScaling > 0 ? window.RenderScaling : 1d;
        var frameDip = window.FrameSize ?? new global::Avalonia.Size(window.Width, window.Height);
        var size = new PixelSize(Math.Max(1, (int)(frameDip.Width * scale)), Math.Max(1, (int)(frameDip.Height * scale)));
        window.Position = new PixelPoint(
            primary.X + Math.Max(0, (primary.Width - size.Width) / 2),
            primary.Y + Math.Max(0, (primary.Height - size.Height) / 2));
        NoteExplicitPlacement(window);
    }

    /// <summary>Union of every connected screen's working area in physical pixels, or null when the
    /// screen device cannot be read yet (headless tests, pre-layout race).</summary>
    private static PixelRect? CurrentScreenWorkingArea(Window window)
    {
        try
        {
            var screens = window.Screens;
            if (screens is null) return null;
            PixelRect? union = null;
            foreach (var screen in screens.All)
                union = union is null ? screen.WorkingArea : union.Value.Union(screen.WorkingArea);
            return union is { } value && value.Width > 0 && value.Height > 0 ? value : null;
        }
        catch (InvalidOperationException)
        {
            return null; // Screen device not ready; restore the saved value.
        }
    }

    /// <summary>HAV-SHELL: corrects a STARTUP placement that left the title strip beyond every screen.
    /// Observed 20/21 November 2026: MainWindow requests CenterScreen at 1440x920 DIP; under this
    /// desktop's fractional scale the initial X11 placement landed at exactly minus the centring
    /// delta ((-767,-348)), putting the whole tab strip above the display on every fresh start.</summary>
    internal static void EnsureReachablePlacement(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.WindowState != WindowState.Normal) return;
        var screens = window.Screens;
        if (screens is null) return;
        var scale = window.RenderScaling > 0 ? window.RenderScaling : 1d;
        var frameDip = window.FrameSize ?? new global::Avalonia.Size(window.Width, window.Height);
        var size = new PixelSize(Math.Max(1, (int)(frameDip.Width * scale)),
                                 Math.Max(1, (int)(frameDip.Height * scale)));
        var areas = screens.All.Select(screen => screen.WorkingArea).ToArray();
        if (ResolveReachablePosition(size, window.Position, areas) is { } corrected)
            window.Position = corrected;
    }

    /// <summary>Returns a corrected physical position when the draggable title strip (a horizontal
    /// band inside the frame's top edge) lies beyond every working area, otherwise null. The window
    /// is re-centred on the area nearest its current centre; windows larger than the area keep their
    /// top-left at the area's corner so the strip always remains reachable and draggable.</summary>
    internal static PixelPoint? ResolveReachablePosition(PixelSize size, PixelPoint position, IReadOnlyList<PixelRect> workingAreas)
    {
        if (workingAreas.Count == 0 || size.Width <= 0 || size.Height <= 0) return null;
        var frame = new PixelRect(position, size);
        PixelRect ReachableStrip(PixelRect f) => new(
            f.X + Math.Min(100, Math.Max(0, f.Width / 4)), f.Y,
            Math.Max(1, Math.Min(200, f.Width / 2)), Math.Min(24, Math.Max(1, f.Height / 4)));
        var strip = ReachableStrip(frame);
        if (workingAreas.Any(area => area.Contains(strip))) return null;
        var area = workingAreas.OrderBy(candidate =>
            DistanceSquared(new PixelPoint(candidate.X + candidate.Width / 2, candidate.Y + candidate.Height / 2),
                            new PixelPoint(frame.X + frame.Width / 2, frame.Y + frame.Height / 2))).First();
        return new PixelPoint(
            Math.Max(area.X, area.X + (area.Width - size.Width) / 2),
            Math.Max(area.Y, area.Y + (area.Height - size.Height) / 2));
    }

    private static double DistanceSquared(PixelPoint a, PixelPoint b) =>
        (double)(a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
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
        return ConfigureMainWindowCustody(RetainWindow(new MainWindow()));
#else
        return ConfigureMainWindowCustody(RetainWindow(new MainWindow(services.GetRequiredService<UserPreferencesService>())));
#endif
    }
}
