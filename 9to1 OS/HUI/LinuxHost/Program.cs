using Avalonia;

namespace CakeOS.HuiLinuxHost;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.RootProvider = HuiRootProviderResolver.Resolve(args, out var avaloniaArgs);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(avaloniaArgs);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
