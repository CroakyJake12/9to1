using Avalonia;

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
        return CakeOS.Cui.Runtime.CuiNativeHost.ConfigureFonts(AppBuilder.Configure<App>())
            .UsePlatformDetect()
            .LogToTrace();
    }
}
