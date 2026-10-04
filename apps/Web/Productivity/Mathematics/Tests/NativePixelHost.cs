using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;

namespace Haven.Desktop.Tests;

// Test infrastructure only. The owner tests reference this existing nested name.
// Its three-line builder is identical to the original PixelAppBuilder in
// shared/tests/Haven.Desktop.Tests/HomeProductivityCuiSurfaceTests.cs. The remaining
// unrelated owner tests are not copied or discovered in this bounded fixture.
public sealed class HomeProductivityCuiSurfaceTests
{
    public static class PixelAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Avalonia.Application>()
            .UseSkia().WithInterFont().With(new Avalonia.Media.FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" }).UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
