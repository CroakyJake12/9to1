using Avalonia;
using CakeOS.Cui.Runtime;

namespace HavenOS.Images;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return CuiNativeHost.ConfigureFonts(AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace());
    }
}
