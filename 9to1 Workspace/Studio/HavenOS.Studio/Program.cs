using Avalonia;
using CakeOS.Cui.Runtime;

namespace HavenOS.AIStudio;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => CuiNativeHost.ConfigureFonts(
        AppBuilder.Configure<StudioNativeApplication>().UsePlatformDetect()).StartWithClassicDesktopLifetime(args);
}
