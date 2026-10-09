using Avalonia;

namespace HavenOS.Apps.Data.Native;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<DataApplication>().UsePlatformDetect().LogToTrace();
}
