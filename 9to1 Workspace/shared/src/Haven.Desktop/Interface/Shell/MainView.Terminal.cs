using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Terminal;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private readonly CancellationTokenSource _terminalLifetime = new();
    private readonly SemaphoreSlim _terminalOpenGate = new(1, 1);

    private void OpenTerminal(bool forceNewTab = false, string? initialDirectory = null) =>
        _ = OpenTerminalAsync(forceNewTab, initialDirectory);

    private async Task OpenTerminalAsync(bool forceNewTab, string? initialDirectory)
    {
        if (IsDisposed) return;
        var token = _terminalLifetime.Token;
        try
        {
            await _terminalOpenGate.WaitAsync(token);
            try
            {
                if (IsDisposed) return;
                if (_terminalPage is { IsClosed: true }) _terminalPage = null;
                var page = !forceNewTab ? _terminalPage : null;
                if (page is null)
                {
                    var services = App.Services ?? throw new InvalidOperationException("Home services are unavailable.");
                    page = await HomeTerminalPageFactory.OpenAsync(services, () => _preferences.CommandPermission,
                        ReviewHomeRequestAsync, initialDirectory, token);
                    if (IsDisposed || token.IsCancellationRequested) { page.Dispose(); return; }
                    if (!forceNewTab) _terminalPage = page;
                }
                var key = forceNewTab ? "terminal-" + Guid.NewGuid().ToString("N")[..8] : "terminal";
                AddOrSelectTab(key, "Terminal", page, forceNewTab, HavenSurface.Terminal, forceNewTab);
                ApplyShellVisualState();
                page.FocusCommandLine();
            }
            finally { _terminalOpenGate.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or InvalidOperationException or NotSupportedException)
        {
            if (!IsDisposed) _notifications.Show("Terminal unavailable", error.Message, ToastKind.Warning, TimeSpan.FromSeconds(5));
        }
    }
}
