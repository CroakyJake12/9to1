using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AvaloniaHome;

internal sealed partial class HomeApp
{
    private TrayIcon? _originalShellTray;
    internal Window? OriginalShellWindow => _workspaceWindow;

    // Both the actual native tray and the owning render control use this SAME retained driver.
    // Reopening a shell borrows the existing owner; it creates no profile, grant or replacement Core.
    internal Task? ShowOriginalShellAsync() => RunOriginalNativeWork(() => RunWorkspaceActionAsync(() =>
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_workspaceWindow?.IsVisible == true)
        {
            _workspaceWindow.Activate();
            return Task.CompletedTask;
        }
        var window = BuildWindow();
        if (_desktop is not null) _desktop.MainWindow = window;
        window.Show();
        return Task.CompletedTask;
    }));

    private void ConfigureOriginalShellTray()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_desktop is null || _originalShellTray is not null)
            throw new InvalidOperationException("The original Home tray requires the one registered desktop owner.");
        // Publish the actual icon before resource loading/property callbacks can fail.
        var tray = _originalShellTray = new TrayIcon();
        using var icon = typeof(HomeApp).Assembly.GetManifestResourceStream("AvaloniaHome.Home.ico")
            ?? throw new InvalidOperationException("The packaged Home tray icon is missing.");
        tray.Icon = new WindowIcon(icon);
        tray.ToolTipText = "9-1 Home — Core keeps running when the window closes";
        var open = new NativeMenuItem("Open Home");
        open.Click += (_, _) => _ = ShowOriginalShellAsync();
        var quit = new NativeMenuItem("Quit Home");
        // Quit is an explicit process request, outside any admitted work that it must join.
        quit.Click += (_, _) => _ = RequestProcessShutdownAsync();
        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(quit);
        tray.Menu = menu;
        tray.Clicked += (_, _) => _ = ShowOriginalShellAsync();
        tray.IsVisible = true;
    }

    private void RetireOriginalShellTray()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_originalShellTray is not { } original) return;
        original.Dispose();
        _originalShellTray = null; // A failed native disposal remains retained and is reported.
    }
}
