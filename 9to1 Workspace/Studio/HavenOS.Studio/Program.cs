using Avalonia;
using CakeOS.Cui.Runtime;

namespace HavenOS.AIStudio;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        int result = 1;
        List<Exception> failures = [];
        try
        {
            result = CuiNativeHost.ConfigureFonts(
                AppBuilder.Configure<StudioNativeApplication>().UsePlatformDetect()).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception primary) { StudioOriginalTaskDrain.Add(failures, primary); }
        if (Application.Current is StudioNativeApplication original)
            try { original.RequireOriginalShutdownSettled(); }
            catch (Exception shutdown) { StudioOriginalTaskDrain.Add(failures, shutdown); }
        else if (failures.Count == 0)
            StudioOriginalTaskDrain.Add(failures, new InvalidOperationException("The original Studio application is unavailable after the native loop."));
        StudioOriginalTaskDrain.Throw(failures);
        return result;
    }
}
