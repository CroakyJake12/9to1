using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Skia;

[assembly: AvaloniaTestApplication(typeof(HavenOS.Apps.Write.Tests.TestAppBuilder))]

namespace HavenOS.Apps.Write.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<WriteApplication>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
