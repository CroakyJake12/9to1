using Avalonia;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Canvas;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<CanvasHostApp>())
        .UseWin32().UseSkia().UseHarfBuzz().LogToTrace();
}
