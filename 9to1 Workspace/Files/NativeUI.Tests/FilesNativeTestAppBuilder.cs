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
        var builder = AppBuilder.Configure<FilesNativeTestApplication>();
        if (capture) builder = builder.UseSkia();
        return CuiNativeHost.ConfigureFonts(builder.UseHeadless(new AvaloniaHeadlessPlatformOptions
        { UseHeadlessDrawing = !capture }));
    }
}

/// <summary>Use the same primitive bootstrap as the production Desktop application and
/// maintained CUI hosts. Internal scroll viewers must receive the real CUI scrolling
/// template rather than Avalonia's inherited plain ContentControl presenter.</summary>
public sealed class FilesNativeTestApplication : Application
{
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Home");
}
