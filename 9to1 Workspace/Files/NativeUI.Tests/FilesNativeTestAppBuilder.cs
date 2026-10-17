using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(HavenOS.Files.NativeUI.Tests.FilesNativeTestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HavenOS.Files.NativeUI.Tests;

/// <summary>Portable component host; no installed Home, desktop shell or platform qualification is implied.</summary>
public static class FilesNativeTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<Application>();
        if (capture) builder = builder.UseSkia();
        return CuiNativeHost.ConfigureFonts(builder.UseHeadless(new AvaloniaHeadlessPlatformOptions
        { UseHeadlessDrawing = !capture }));
    }
}
