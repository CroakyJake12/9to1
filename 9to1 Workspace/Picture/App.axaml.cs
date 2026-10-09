using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CakeOS.Cui.Runtime;

namespace HavenOS.Images;

public sealed partial class App : Application
{
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Imagine");

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            _ = StartAsync(window);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartAsync(MainWindow window)
    {
        try { await window.InitializeAsync(); }
        catch (Exception error)
        {
            // The actual scene retains its unavailable/readiness state. A failed
            // Home handshake never mounts the local editing actions.
            System.Diagnostics.Trace.TraceError("Picture startup failed: {0}", error);
        }
    }
}
