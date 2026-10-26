using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Tests;

return CuiNativeHost.ConfigureFonts(AppBuilder.Configure<NativePopupProbeApplication>())
    .UsePlatformDetect()
    .StartWithClassicDesktopLifetime(args);

internal sealed class NativePopupProbeApplication : Application
{
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Home");

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            throw new InvalidOperationException("The native popup probe requires the classic desktop lifetime.");

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () =>
        {
            var exitCode = 0;
            try
            {
                await ChatAddMenuNativePopupTests.RunAsync(requireNativePopup: true,
                    captureDirectory: Environment.GetEnvironmentVariable("HAVEN_NATIVE_POPUP_CAPTURE_DIR"));
                Console.WriteLine("Native chat Add menu popup regression passed.");
            }
            catch (Exception error)
            {
                exitCode = 1;
                Console.Error.WriteLine(error);
            }
            finally
            {
                desktop.Shutdown(exitCode);
            }
        });

        base.OnFrameworkInitializationCompleted();
    }
}
