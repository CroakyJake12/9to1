using Avalonia;
using CakeOS.Cui.Runtime;

namespace HavenOS.AIStudio;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var actualExitCode = CuiNativeHost.ConfigureFonts(
            AppBuilder.Configure<StudioNativeApplication>().UsePlatformDetect()).StartWithClassicDesktopLifetime(args);
        // Never wait for a pending UI task after its native loop stopped.
        var original = StudioNativeApplication.OriginalRunningApplication;
        return original is null ? (actualExitCode == 0 ? 1 : actualExitCode) :
            original.VerifyOriginalNativeExit(actualExitCode);
    }
}
